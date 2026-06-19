using Flit.Admin.Domain.ProcedureSnapshots;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de las instancias de trámite (HU #10197, RF19).
///
/// <see cref="CreateWithSnapshotAsync"/> agrega el trámite y su snapshot al contexto y los
/// persiste con un <b>único</b> <c>SaveChangesAsync</c>: EF/Npgsql lo ejecuta en una sola
/// transacción implícita, por lo que un fallo no deja ni el trámite ni el snapshot a medias
/// (AC3). Sobre el proveedor InMemory de los tests rige la misma atomicidad: las entidades
/// solo se materializan si el save tiene éxito.
/// </summary>
internal sealed class ProcedureInstanceRepository : IProcedureInstanceRepository
{
    private readonly FlitDbContext _context;

    public ProcedureInstanceRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.ProcedureInstances
            .AsNoTracking()
            .AnyAsync(i => i.Id == id, cancellationToken);

    public Task<ProcedureInstanceRecord?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        _context.ProcedureInstances
            .AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new ProcedureInstanceRecord(
                i.Id,
                i.TenantId,
                i.ProcedureTypeId,
                i.ReferenceNumber,
                i.Status,
                i.TransitOfficeId,
                i.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ReferenceNumberExistsAsync(
        Guid tenantId,
        string referenceNumber,
        CancellationToken cancellationToken = default) =>
        _context.ProcedureInstances
            .AsNoTracking()
            .AnyAsync(
                i => i.TenantId == tenantId && i.ReferenceNumber == referenceNumber,
                cancellationToken);

    public async Task<CreatedProcedureInstance> CreateWithSnapshotAsync(
        NewProcedureInstance instance,
        string snapshotJson,
        Guid? snapshotBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var now = DateTimeOffset.UtcNow;

        var procedureInstance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureTypeId = instance.ProcedureTypeId,
            ReferenceNumber = instance.ReferenceNumber,
            Status = "draft",
            TransitOfficeId = instance.TransitOfficeId,
            CreatedByUserId = instance.CreatedByUserId,
            CreatedAt = now,
        };

        var snapshot = new ProcedureDocumentSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = procedureInstance.Id,
            Snapshot = snapshotJson,
            SnapshotAt = now,
            SnapshotBy = snapshotBy,
        };

        _context.ProcedureInstances.Add(procedureInstance);
        _context.ProcedureDocumentSnapshots.Add(snapshot);

        // Único punto de persistencia: trámite + snapshot en una sola transacción (AC3).
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new CreatedProcedureInstance(
            procedureInstance.Id,
            procedureInstance.ReferenceNumber,
            procedureInstance.Status,
            procedureInstance.CreatedAt,
            snapshot.SnapshotAt);
    }
}
