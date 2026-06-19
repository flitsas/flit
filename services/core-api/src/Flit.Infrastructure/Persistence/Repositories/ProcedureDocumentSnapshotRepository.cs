using Flit.Admin.Domain.ProcedureSnapshots;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de la lectura del snapshot documental (HU #10197, AC2/AC4).
/// Devuelve el JSON crudo de <c>tramites.procedure_document_snapshots</c> para que la capa
/// de aplicación lo deserialice; nunca lo modifica (inmutable).
/// </summary>
internal sealed class ProcedureDocumentSnapshotRepository : IProcedureDocumentSnapshotRepository
{
    private readonly FlitDbContext _context;

    public ProcedureDocumentSnapshotRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<ProcedureDocumentSnapshotRecord?> GetByProcedureInstanceIdAsync(
        Guid procedureInstanceId,
        CancellationToken cancellationToken = default) =>
        _context.ProcedureDocumentSnapshots
            .AsNoTracking()
            .Where(s => s.ProcedureInstanceId == procedureInstanceId)
            .Select(s => new ProcedureDocumentSnapshotRecord(
                s.Id,
                s.ProcedureInstanceId,
                s.TenantId,
                s.Snapshot,
                s.SnapshotAt))
            .FirstOrDefaultAsync(cancellationToken);
}
