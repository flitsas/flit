using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Resuelve plantilla/custom del OT + assignment_mode de la regla compañía×OT (default signer).
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

        // Sin fila de OT: aún podemos resolver solo el tipo si hay office id por código + regla compañía.
        Guid officeId;
        if (otRow is null)
        {
            var office = await _context.TransitOffices.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Code == code, cancellationToken)
                .ConfigureAwait(false);
            if (office is null)
                return null;

            officeId = office.Id;
            var modeOnly = await ResolveAssignmentModeAsync(officeId, companyTenantId, cancellationToken)
                .ConfigureAwait(false);

            return new MandateOtConfig(
                officeId,
                MandatoTemplateResolver.Generico,
                RequiresForNaturalPerson: false,
                InstitutionalMandataryName: null,
                InstitutionalMandataryNit: null,
                MandataryFamily: MandatoFamiliaCodes.Individuo,
                ChamberCity: null,
                MandatarySigla: null,
                AssignmentMode: modeOnly);
        }

        officeId = otRow.TransitOfficeId;
        var assignmentMode = await ResolveAssignmentModeAsync(officeId, companyTenantId, cancellationToken)
            .ConfigureAwait(false);

        var rule = companyTenantId is { } companyId
            ? await _context.CompanyOtMandateRules.AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.TransitOfficeId == officeId && r.CompanyTenantId == companyId,
                    cancellationToken)
                .ConfigureAwait(false)
            : null;

        return new MandateOtConfig(
            otRow.TransitOfficeId,
            otRow.TemplateCode,
            otRow.RequiresForNaturalPerson,
            rule?.InstitutionalMandataryName ?? otRow.InstitutionalMandataryName,
            rule?.InstitutionalMandataryNit ?? otRow.InstitutionalMandataryNit,
            rule?.MandataryFamily ?? otRow.MandataryFamily,
            rule?.ChamberCity ?? otRow.ChamberCity,
            rule?.MandatarySigla ?? otRow.MandatarySigla,
            assignmentMode,
            otRow.CustomTemplateKind,
            otRow.CustomTemplateBody,
            otRow.CustomTemplateStoragePath,
            otRow.CustomTemplateFileName);
    }

    private async Task<string> ResolveAssignmentModeAsync(
        Guid officeId,
        Guid? companyTenantId,
        CancellationToken cancellationToken)
    {
        if (companyTenantId is not { } companyId)
            return MandatoAssignmentModeCodes.Signer;

        var mode = await _context.CompanyOtMandateRules.AsNoTracking()
            .Where(r => r.TransitOfficeId == officeId && r.CompanyTenantId == companyId)
            .Select(r => r.AssignmentMode)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return MandatoAssignmentModeCodes.Resolve(mode);
    }
}
