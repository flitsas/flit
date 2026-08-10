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

        var otRow = await (
            from cfg in _context.TransitOfficeMandateConfigs.AsNoTracking()
            join ot in _context.TransitOffices.AsNoTracking() on cfg.TransitOfficeId equals ot.Id
            where ot.Code == code
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
                cfg.CustomTemplateKind,
                cfg.CustomTemplateBody,
                cfg.CustomTemplateStoragePath,
                cfg.CustomTemplateFileName,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        Guid officeId;
        if (otRow is null)
        {
            var office = await _context.TransitOffices.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Code == code, cancellationToken)
                .ConfigureAwait(false);
            if (office is null)
                return null;

            officeId = office.Id;
            var ruleOnly = await LoadCompanyRuleAsync(officeId, companyTenantId, cancellationToken)
                .ConfigureAwait(false);
            var builtin = MandatoSystemOfficeTemplates.TryGetByOfficeCode(code);

            return new MandateOtConfig(
                officeId,
                MandatoSystemOfficeTemplates.ResolveTemplateCode(code, null, null),
                builtin?.RequiresForNaturalPerson ?? false,
                builtin?.InstitutionalMandataryName,
                builtin?.InstitutionalMandataryNit,
                builtin?.MandataryFamily ?? MandatoFamiliaCodes.Individuo,
                builtin?.ChamberCity,
                builtin?.MandatarySigla,
                MandatoAssignmentModeCodes.Resolve(ruleOnly?.AssignmentMode),
                DefaultMandateSignerId: SignerDefaultOrNull(ruleOnly));
        }

        officeId = otRow.TransitOfficeId;
        var rule = await LoadCompanyRuleAsync(officeId, companyTenantId, cancellationToken)
            .ConfigureAwait(false);
        var hasCustom = MandatoCustomTemplateKindCodes.HasCustom(otRow.CustomTemplateKind);
        var builtinForOffice = MandatoSystemOfficeTemplates.TryGetByOfficeCode(code);
        var templateCode = MandatoSystemOfficeTemplates.ResolveTemplateCode(
            code, otRow.TemplateCode, otRow.CustomTemplateKind);

        // Metadatos institucionales: fila > builtin sistema (Sabaneta/Bello) > null.
        var family = !string.IsNullOrWhiteSpace(rule?.MandataryFamily)
            ? rule!.MandataryFamily
            : !string.IsNullOrWhiteSpace(otRow.MandataryFamily)
                ? otRow.MandataryFamily
                : builtinForOffice?.MandataryFamily ?? MandatoFamiliaCodes.Individuo;

        return new MandateOtConfig(
            otRow.TransitOfficeId,
            templateCode,
            otRow.RequiresForNaturalPerson || (builtinForOffice?.RequiresForNaturalPerson ?? false),
            rule?.InstitutionalMandataryName
                ?? otRow.InstitutionalMandataryName
                ?? builtinForOffice?.InstitutionalMandataryName,
            rule?.InstitutionalMandataryNit
                ?? otRow.InstitutionalMandataryNit
                ?? builtinForOffice?.InstitutionalMandataryNit,
            family,
            rule?.ChamberCity ?? otRow.ChamberCity ?? builtinForOffice?.ChamberCity,
            rule?.MandatarySigla ?? otRow.MandatarySigla ?? builtinForOffice?.MandatarySigla,
            MandatoAssignmentModeCodes.Resolve(rule?.AssignmentMode),
            hasCustom ? otRow.CustomTemplateKind : MandatoCustomTemplateKindCodes.None,
            hasCustom ? otRow.CustomTemplateBody : null,
            hasCustom ? otRow.CustomTemplateStoragePath : null,
            hasCustom ? otRow.CustomTemplateFileName : null,
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
}
