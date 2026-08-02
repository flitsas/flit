using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Implementación del puerto <see cref="IMandateRequirementPolicy"/> (ADR-0036, HU #10912). Resuelve la
/// configuración de mandato del OT por su <b>código</b> (join <c>admin.transit_office_mandate_config</c>
/// ↔ <c>catalogs.transit_offices</c>). Ambas tablas son catálogo/config sin <c>tenant_id</c> ni RLS, así
/// que la lectura es directa (sin <c>row_security = off</c>). Devuelve <c>null</c> si el OT no tiene
/// fila de configuración: el consumidor aplica el default (plantilla genérica + solo persona jurídica).
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
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transitOfficeCode))
        {
            return null;
        }

        var code = transitOfficeCode.Trim();

        return await (
            from cfg in _context.TransitOfficeMandateConfigs.AsNoTracking()
            join ot in _context.TransitOffices.AsNoTracking() on cfg.TransitOfficeId equals ot.Id
            where ot.Code == code
            select new MandateOtConfig(
                cfg.TransitOfficeId,
                cfg.TemplateCode,
                cfg.RequiresForNaturalPerson,
                cfg.InstitutionalMandataryName,
                cfg.InstitutionalMandataryNit,
                cfg.MandataryFamily,
                cfg.ChamberCity,
                cfg.MandatarySigla))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
