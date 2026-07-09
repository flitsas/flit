using Flit.Admin.Domain.ProcedureSnapshots;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core del repositorio de instancias del módulo Admin (HU #10197, RF19).
///
/// <para>Tras el merge del rework de trámites (#10128), la entidad canónica
/// <see cref="ProcedureInstance"/> (<c>Flit.Tramites.Domain.Entities</c>) es la del runtime
/// completo y mapea <c>tramites.procedure_instances</c>. Este repositorio del snapshot
/// documental (HU #10197) opera sobre esa misma entidad: la entidad mínima de persistencia
/// que tenía develop se eliminó para evitar dos tipos CLR sobre la misma tabla.</para>
///
/// <see cref="CreateWithSnapshotAsync"/> agrega el trámite y su snapshot al contexto y los
/// persiste con un <b>único</b> <c>SaveChangesAsync</c>: EF/Npgsql lo ejecuta en una sola
/// transacción implícita, por lo que un fallo no deja ni el trámite ni el snapshot a medias
/// (AC3). Sobre el proveedor InMemory de los tests rige la misma atomicidad.
/// </summary>
internal sealed class AdminProcedureInstanceRepository : IProcedureInstanceRepository
{
    private readonly FlitDbContext _context;

    public AdminProcedureInstanceRepository(FlitDbContext context)
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
            Status = Flit.Tramites.Domain.Tramites.Estados.TramiteEstado.Borrador,
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
