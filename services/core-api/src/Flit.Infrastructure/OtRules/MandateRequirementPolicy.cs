using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Resuelve plantilla/custom del OT + assignment_mode / default signer de la regla compañía×OT.
/// Prioridad de plantilla: propia cargada → builtin Sabaneta/Bello → config de otro OT → genérica.
/// </summary>
internal sealed class MandateRequirementPolicy : IMandateRequirementPolicy
{
    private readonly FlitDbContext _context;

    public MandateRequirementPolicy(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<MandateOtConfig?> ResolveAsync(
        string transitOfficeCode,
        Guid? companyTenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transitOfficeCode))
        {
            return null;
        }

        var code = transitOfficeCode.Trim();
        var office = await _context.TransitOffices.AsNoTracking()
            .Where(o => o.Code == code)
            .Select(o => new { o.Id, o.Code })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return office is null
            ? null
            : await ResolveCoreAsync(office.Id, office.Code, companyTenantId, cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<MandateOtConfig?> ResolveByOfficeIdAsync(
        Guid transitOfficeId,
        Guid? companyTenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (transitOfficeId == Guid.Empty)
        {
            return null;
        }

        var office = await _context.TransitOffices.AsNoTracking()
            .Where(o => o.Id == transitOfficeId)
            .Select(o => new { o.Id, o.Code })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return office is null
            ? null
            : await ResolveCoreAsync(office.Id, office.Code, companyTenantId, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Núcleo compartido: el organismo ya está identificado (id + código canónico del catálogo), así que
    /// el builtin se coteja contra el código del CATÁLOGO y no contra el que venga del trámite.
    /// </summary>
    private async Task<MandateOtConfig?> ResolveCoreAsync(
        Guid officeId,
        string code,
        Guid? companyTenantId,
        CancellationToken cancellationToken)
    {
        var otRow = await (
            from cfg in _context.TransitOfficeMandateConfigs.AsNoTracking()
            where cfg.TransitOfficeId == officeId
            select new
            {
                cfg.TransitOfficeId,
                cfg.TemplateCode,
                cfg.RequiresForNaturalPerson,
                cfg.InstitutionalMandataryName,
                cfg.InstitutionalMandataryNit,
                cfg.MandataryFamily,
                cfg.ChamberCity,
                cfg.MandatarySigla,
                cfg.AssignmentMode,
                cfg.DefaultMandateSignerId,
                cfg.CustomTemplateKind,
                cfg.CustomTemplateBody,
                cfg.CustomTemplateStoragePath,
                cfg.CustomTemplateFileName,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var rule = await LoadCompanyRuleAsync(officeId, companyTenantId, cancellationToken)
            .ConfigureAwait(false);
        var assignmentMode = MandatoAssignmentModeCodes.ResolveEffective(
            rule?.AssignmentMode, otRow?.AssignmentMode, otConfigExists: otRow is not null);
        var companySigner = rule is not null
            && MandatoAssignmentModeCodes.Resolve(rule.AssignmentMode) == MandatoAssignmentModeCodes.Signer;

        if (otRow is null)
        {
            var builtin = MandatoSystemOfficeTemplates.TryGetByOfficeCode(code);
            return new MandateOtConfig(
                officeId,
                ResolveClientTemplate(
                    companySigner,
                    MandatoSystemOfficeTemplates.ResolveTemplateCode(code, null, null),
                    hasCustom: false),
                builtin?.RequiresForNaturalPerson ?? false,
                OpenOrValue(assignmentMode, builtin?.InstitutionalMandataryName),
                OpenOrValue(assignmentMode, builtin?.InstitutionalMandataryNit),
                builtin?.MandataryFamily ?? MandatoFamiliaCodes.Individuo,
                builtin?.ChamberCity,
                builtin?.MandatarySigla,
                assignmentMode,
                OtDefaultMandateSignerId: OtDefaultOrNull(assignmentMode, null),
                DefaultMandateSignerId: SignerDefaultOrNull(rule));
        }

        var hasCustom = MandatoCustomTemplateKindCodes.HasCustom(otRow.CustomTemplateKind);
        var builtinForOffice = MandatoSystemOfficeTemplates.TryGetByOfficeCode(code);
        var templateCode = ResolveClientTemplate(
            companySigner,
            MandatoSystemOfficeTemplates.ResolveTemplateCode(
                code, otRow.TemplateCode, otRow.CustomTemplateKind),
            hasCustom);

        var family = !string.IsNullOrWhiteSpace(rule?.MandataryFamily)
            ? rule!.MandataryFamily
            : !string.IsNullOrWhiteSpace(otRow.MandataryFamily)
                ? otRow.MandataryFamily
                : builtinForOffice?.MandataryFamily ?? MandatoFamiliaCodes.Individuo;

        return new MandateOtConfig(
            otRow.TransitOfficeId,
            templateCode,
            otRow.RequiresForNaturalPerson || (builtinForOffice?.RequiresForNaturalPerson ?? false),
            OpenOrValue(
                assignmentMode,
                rule?.InstitutionalMandataryName
                    ?? otRow.InstitutionalMandataryName
                    ?? builtinForOffice?.InstitutionalMandataryName),
            OpenOrValue(
                assignmentMode,
                rule?.InstitutionalMandataryNit
                    ?? otRow.InstitutionalMandataryNit
                    ?? builtinForOffice?.InstitutionalMandataryNit),
            family,
            rule?.ChamberCity ?? otRow.ChamberCity ?? builtinForOffice?.ChamberCity,
            rule?.MandatarySigla ?? otRow.MandatarySigla ?? builtinForOffice?.MandatarySigla,
            assignmentMode,
            hasCustom ? otRow.CustomTemplateKind : MandatoCustomTemplateKindCodes.None,
            hasCustom ? otRow.CustomTemplateBody : null,
            hasCustom ? otRow.CustomTemplateStoragePath : null,
            hasCustom ? otRow.CustomTemplateFileName : null,
            OtDefaultOrNull(assignmentMode, otRow.DefaultMandateSignerId),
            SignerDefaultOrNull(rule));
    }

    private async Task<CompanyOtMandateRuleEntity?> LoadCompanyRuleAsync(
        Guid officeId,
        Guid? companyTenantId,
        CancellationToken cancellationToken)
    {
        if (companyTenantId is not { } companyId)
            return null;

        return await _context.CompanyOtMandateRules.AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.TransitOfficeId == officeId && r.CompanyTenantId == companyId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static Guid? SignerDefaultOrNull(CompanyOtMandateRuleEntity? rule)
    {
        if (rule is null)
            return null;
        if (MandatoAssignmentModeCodes.SkipsPersonSigner(rule.AssignmentMode))
            return null;
        return rule.DefaultMandateSignerId;
    }

    private static Guid? OtDefaultOrNull(string assignmentMode, Guid? otDefault)
    {
        if (MandatoAssignmentModeCodes.SkipsPersonSigner(assignmentMode))
            return null;
        return otDefault is { } id && id != Guid.Empty ? id : null;
    }

    private static string? OpenOrValue(string assignmentMode, string? value) =>
        MandatoAssignmentModeCodes.IsOpen(assignmentMode) ? null : value;

    /// <summary>
    /// Mandato de la empresa que radica (<c>signer</c>): plantilla genérica, salvo PDF/editor propio.
    /// </summary>
    private static string ResolveClientTemplate(bool companySigner, string templateCode, bool hasCustom)
    {
        if (hasCustom)
            return templateCode;
        if (companySigner)
            return MandatoTemplateResolver.Generico;
        return templateCode;
    }
}
