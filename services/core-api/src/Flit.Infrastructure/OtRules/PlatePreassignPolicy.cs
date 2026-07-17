using Flit.Admin.Domain.PlatePreassign;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Tramites.Enums;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Decide la ruta de preasignación de placa al radicar (HU #10608, Feature #10587). Solo matrícula
/// inicial; exige preasignación activa (flag de la compañía + grant + allow_plate_preassign del OT).
/// Con placa elegida y disponible la reserva y aterriza en <c>asignado</c> (Flujo A); sin rango/placa,
/// <c>preasignado</c> (Flujo B). Patrón de <see cref="RnmcRequirementPolicy"/>.
/// </summary>
internal sealed class PlatePreassignPolicy : IPlatePreassignPolicy
{
    private const string OfficeFieldKey = "transit_office_id";
    private const string PlateFieldKey = "plate";

    private readonly FlitDbContext _context;
    private readonly IPlateRangeRepository _plateRepo;

    public PlatePreassignPolicy(FlitDbContext context, IPlateRangeRepository plateRepo)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _plateRepo = plateRepo ?? throw new ArgumentNullException(nameof(plateRepo));
    }

    public async Task<PlateRouteDecision> DecideAsync(
        Guid tenantId,
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        var modalidad = await _context.ProcedureInstances
            .AsNoTracking()
            .Where(p => p.Id == instanceId && p.TenantId == tenantId)
            .Select(p => p.ModalidadEntrada)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (modalidad is null
            || TramiteModalidadEntradaCodes.FromCode(modalidad) != TramiteModalidadEntrada.MatriculaInicial)
        {
            return PlateRouteDecision.Standard;
        }

        var fields = await _context.ProcedureInstanceFieldValues
            .AsNoTracking()
            .Where(f => f.ProcedureInstanceId == instanceId
                && (f.FieldKey == OfficeFieldKey || f.FieldKey == PlateFieldKey))
            .Select(f => new { f.FieldKey, f.ValueText })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var officeRaw = fields.FirstOrDefault(f => f.FieldKey == OfficeFieldKey)?.ValueText;
        if (!Guid.TryParse(officeRaw, out var officeId) || officeId == Guid.Empty)
        {
            return PlateRouteDecision.Standard;
        }

        var allowed = await _plateRepo.IsAssignmentAllowedAsync(tenantId, officeId, cancellationToken)
            .ConfigureAwait(false);
        if (!allowed)
        {
            return PlateRouteDecision.Standard;
        }

        var plate = fields.FirstOrDefault(f => f.FieldKey == PlateFieldKey)?.ValueText;
        if (!string.IsNullOrWhiteSpace(plate))
        {
            var reserved = await _plateRepo
                .TryReservePlateAsync(tenantId, officeId, plate, instanceId, cancellationToken)
                .ConfigureAwait(false);
            if (reserved)
            {
                return PlateRouteDecision.Asignado;
            }
        }

        // Ruta activa pero sin placa disponible elegida → se envía al OT para que asigne (Flujo B).
        return PlateRouteDecision.Preasignado;
    }
}
