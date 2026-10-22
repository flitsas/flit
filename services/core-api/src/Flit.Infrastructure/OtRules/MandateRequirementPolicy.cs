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
        var effectiveSigner = MandatoAssignmentModeCodes.Resolve(assignmentMode) == MandatoAssignmentModeCodes.Signer;

        if (otRow is null)
        {
            var builtin = MandatoSystemOfficeTemplates.TryGetByOfficeCode(code);
            var builtinTemplate = MandatoSystemOfficeTemplates.ResolveTemplateCode(code, null, null);
            // Sin fila de OT el «signer» es solo el default del legado (Sabaneta sigue institucional): aquí solo
            // cuenta la regla explícita de la compañía.
            var naturalSinOt = NaturalPersonMandate(
                companySigner, effectiveSigner: false, builtinTemplate, hasCustom: false);
            return new MandateOtConfig(
                officeId,
                ResolveClientTemplate(naturalSinOt, builtinTemplate, hasCustom: false),
                builtin?.RequiresForNaturalPerson ?? false,
                // HU #13154 — sin fila de OT (legado) la regla de la compañía igual manda: antes se ignoraba y el
                // contrato de una compañía en «Persona jurídica» no citaba a su entidad.
                OpenOrValue(
                    assignmentMode,
                    rule?.InstitutionalMandataryName ?? (naturalSinOt ? null : builtin?.InstitutionalMandataryName)),
                OpenOrValue(
                    assignmentMode,
                    rule?.InstitutionalMandataryNit ?? (naturalSinOt ? null : builtin?.InstitutionalMandataryNit)),
                !string.IsNullOrWhiteSpace(rule?.MandataryFamily)
                    ? rule!.MandataryFamily
                    : naturalSinOt
                        ? MandatoFamiliaCodes.Individuo
                        : builtin?.MandataryFamily ?? MandatoFamiliaCodes.Individuo,
                rule?.ChamberCity ?? builtin?.ChamberCity,
                rule?.MandatarySigla ?? builtin?.MandatarySigla,
                assignmentMode,
                OtDefaultMandateSignerId: OtDefaultOrNull(assignmentMode, null),
                DefaultMandateSignerId: SignerDefaultOrNull(rule));
        }

        var hasCustom = MandatoCustomTemplateKindCodes.HasCustom(otRow.CustomTemplateKind);
        var builtinForOffice = MandatoSystemOfficeTemplates.TryGetByOfficeCode(code);
        var otTemplate = MandatoSystemOfficeTemplates.ResolveTemplateCode(
            code, otRow.TemplateCode, otRow.CustomTemplateKind);
        // HU #13154b — Persona natural (regla de la compañía o heredada del OT) sobre una redacción institucional
        // (Sabaneta): el contrato cita al mandatario persona resuelto, no a la UT del organismo.
        var natural = NaturalPersonMandate(companySigner, effectiveSigner, otTemplate, hasCustom);
        var templateCode = ResolveClientTemplate(natural, otTemplate, hasCustom);

        var family = !string.IsNullOrWhiteSpace(rule?.MandataryFamily)
            ? rule!.MandataryFamily
            : natural
                ? MandatoFamiliaCodes.Individuo
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
                    ?? (natural ? null : otRow.InstitutionalMandataryName ?? builtinForOffice?.InstitutionalMandataryName)),
            OpenOrValue(
                assignmentMode,
                rule?.InstitutionalMandataryNit
                    ?? (natural ? null : otRow.InstitutionalMandataryNit ?? builtinForOffice?.InstitutionalMandataryNit)),
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

    /// <summary>
    /// ¿El mandato debe citar a una persona natural como mandatario? Sí cuando la regla de la compañía es
    /// Persona natural (siempre), o cuando el tipo efectivo es Persona natural y la redacción del organismo es
    /// la institucional (Sabaneta), que de otro modo pintaría a la UT. Con plantilla propia no se toca.
    /// </summary>
    private static bool NaturalPersonMandate(
        bool companySigner, bool effectiveSigner, string templateCode, bool hasCustom) =>
        !hasCustom
        && (companySigner
            || (effectiveSigner
                && MandatoTemplateResolver.Resolve(templateCode) == MandatoVariante.Sabaneta));

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
    private static string ResolveClientTemplate(bool naturalPerson, string templateCode, bool hasCustom)
    {
        if (hasCustom)
            return templateCode;
        if (naturalPerson)
            return MandatoTemplateResolver.Generico;
        return templateCode;
    }
}
