using Flit.Admin.Domain.DocumentRequirements;
using Flit.Admin.Domain.ProcedureSnapshots;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Services;

/// <summary>
/// Implementación real del guard de uso de una asociación trámite ↔ documento
/// (HU #10195 AC4, cerrada por HU #10197 RF19). Reemplaza al stub transitorio.
///
/// Una asociación está "en uso" si algún trámite <b>activo</b> (estado distinto de
/// <c>completed</c>/<c>cancelled</c>) del mismo tipo de trámite tiene un snapshot documental
/// que congeló ese tipo de documento. Como el snapshot es inmutable, borrar la asociación
/// no afecta a los trámites ya creados, pero sí debe bloquearse para conservar la integridad
/// del catálogo mientras existan trámites activos que lo referencian.
/// </summary>
internal sealed class ProcedureDocumentRequirementUsageGuard : IProcedureDocumentRequirementUsageGuard
{
    private static readonly string[] InactiveStatuses = ["completed", "cancelled"];

    private readonly FlitDbContext _context;

    public ProcedureDocumentRequirementUsageGuard(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<bool> IsInUseAsync(
        Guid requirementId,
        CancellationToken cancellationToken = default)
    {
        var requirement = await _context.ProcedureDocumentRequirements
            .AsNoTracking()
            .Where(r => r.Id == requirementId)
            .Select(r => new { r.ProcedureTypeId, r.DocumentTypeId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (requirement is null)
        {
            return false;
        }

        // Snapshots de trámites activos del mismo tipo de trámite. El filtro por tipo de
        // trámite acota la búsqueda; la pertenencia del documento se evalúa en el JSON.
        var snapshots = await (
                from s in _context.ProcedureDocumentSnapshots.AsNoTracking()
                join i in _context.ProcedureInstances.AsNoTracking()
                    on s.ProcedureInstanceId equals i.Id
                where i.ProcedureTypeId == requirement.ProcedureTypeId
                    && !InactiveStatuses.Contains(i.Status)
                select s.Snapshot)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var json in snapshots)
        {
            var payload = SnapshotJson.Deserialize(json);
            if (payload is not null
                && payload.Documentos.Any(d => d.DocumentTypeId == requirement.DocumentTypeId))
            {
                return true;
            }
        }

        return false;
    }
}
