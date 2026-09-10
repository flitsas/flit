using Flit.Admin.Domain.GeneracionDocumental;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio de <c>admin.standalone_document_batches</c> (Feature #12201, I3).
///
/// <para>Las consultas del usuario filtran SIEMPRE por <c>tenant_id</c> y por
/// <c>deleted_at IS NULL</c>: ese filtro —más el ownership check del endpoint— es el aislamiento
/// EFECTIVO. La política RLS de la tabla no aísla nada (sin <c>FORCE ROW LEVEL SECURITY</c> y con la
/// app conectando como owner) y ningún test debe apoyarse en ella.</para>
///
/// <para><see cref="ClaimNextAsync"/> es la ÚNICA excepción al filtro por tenant, y es deliberada:
/// el worker es un servicio de plataforma que atiende lotes de todas las compañías. Por eso el DDL
/// define el índice parcial <c>ix_standalone_document_batches_pendientes</c> sin <c>tenant_id</c> al
/// frente. El tenant se lee de la fila reclamada, no del llamador.</para>
/// </summary>
internal sealed class StandaloneDocumentBatchRepository : IStandaloneDocumentBatchRepository
{
    private readonly FlitDbContext _context;

    public StandaloneDocumentBatchRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<StandaloneDocumentBatch?> FindByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        var entity = await Scoped(tenantId)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : Map(entity);
    }

    public async Task<StandaloneDocumentBatch?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await Scoped(tenantId)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : Map(entity);
    }

    public async Task InsertAsync(
        StandaloneDocumentBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var entity = new StandaloneDocumentBatchEntity
        {
            Id = batch.Id == Guid.Empty ? Guid.CreateVersion7() : batch.Id,
            TenantId = batch.TenantId,
            CreatedByUserId = batch.CreatedByUserId,
            Status = batch.Status,
            TemplateVersion = batch.TemplateVersion,
            SourceFilename = batch.SourceFilename,
            SourceStoragePath = batch.SourceStoragePath,
            SourceSha256 = batch.SourceSha256,
            TotalItems = batch.TotalItems,
            GeneratedCount = batch.GeneratedCount,
            ErrorCount = batch.ErrorCount,
            IdempotencyKey = batch.IdempotencyKey,
            ClaimedAt = null,
            CompletedAt = null,
            CreatedAt = batch.CreatedAt == default ? DateTimeOffset.UtcNow : batch.CreatedAt,
            CreatedBy = batch.CreatedByUserId,
        };

        _context.StandaloneDocumentBatches.Add(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Los updates posteriores van por ExecuteUpdateAsync (SQL directo): la copia rastreada no
        // debe quedarse compitiendo con ellos en el ChangeTracker.
        _context.Entry(entity).State = EntityState.Detached;
    }

    /// <summary>
    /// Toma de lote y reaper en la misma operación (R5). El UPDATE lleva la MISMA condición que la
    /// selección: si otro worker se adelantó, afecta 0 filas y aquí se devuelve <c>null</c> en vez
    /// de procesar un lote ajeno. Es un claim optimista, no un <c>SELECT</c> seguido de un
    /// <c>UPDATE</c> incondicional.
    /// </summary>
    public async Task<StandaloneDocumentBatch?> ClaimNextAsync(
        DateTimeOffset now,
        DateTimeOffset reclaimBefore,
        CancellationToken cancellationToken = default)
    {
        // Expresión ÚNICA reutilizada en la selección y en el UPDATE: si se escribieran dos veces
        // podrían divergir y el claim dejaría de ser atómico. Va como Expression y no como método
        // para que EF la traduzca a SQL (un método estático no es traducible).
        System.Linq.Expressions.Expression<Func<StandaloneDocumentBatchEntity, bool>> reclamable =
            x => x.DeletedAt == null
                && (x.Status == StandaloneDocumentBatchStatus.Queued
                    || (x.Status == StandaloneDocumentBatchStatus.Processing
                        && x.ClaimedAt != null
                        && x.ClaimedAt < reclaimBefore));

        var candidato = await _context.StandaloneDocumentBatches
            .Where(reclamable)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidato == Guid.Empty)
        {
            return null;
        }

        var afectadas = await _context.StandaloneDocumentBatches
            .Where(reclamable)
            .Where(x => x.Id == candidato)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.Status, StandaloneDocumentBatchStatus.Processing)
                    .SetProperty(x => x.ClaimedAt, now)
                    .SetProperty(x => x.UpdatedAt, now),
                cancellationToken)
            .ConfigureAwait(false);

        if (afectadas == 0)
        {
            return null;
        }

        var entity = await _context.StandaloneDocumentBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == candidato, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : Map(entity);
    }

    public Task CompleteAsync(
        Guid id,
        string status,
        int generatedCount,
        int errorCount,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        // El CHECK ck_standalone_document_batches_cierre exige completed_at en los tres estados
        // terminales: se escriben en el MISMO update, nunca en dos pasos.
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        return _context.StandaloneDocumentBatches
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.Status, status)
                    .SetProperty(x => x.GeneratedCount, generatedCount)
                    .SetProperty(x => x.ErrorCount, errorCount)
                    .SetProperty(x => x.CompletedAt, completedAt)
                    .SetProperty(x => x.UpdatedAt, completedAt),
                cancellationToken);
    }

    private IQueryable<StandaloneDocumentBatchEntity> Scoped(Guid tenantId) =>
        _context.StandaloneDocumentBatches.Where(x => x.TenantId == tenantId && x.DeletedAt == null);

    private static StandaloneDocumentBatch Map(StandaloneDocumentBatchEntity e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        CreatedByUserId = e.CreatedByUserId,
        Status = e.Status,
        TemplateVersion = e.TemplateVersion,
        SourceFilename = e.SourceFilename,
        SourceStoragePath = e.SourceStoragePath,
        SourceSha256 = e.SourceSha256,
        TotalItems = e.TotalItems,
        GeneratedCount = e.GeneratedCount,
        ErrorCount = e.ErrorCount,
        IdempotencyKey = e.IdempotencyKey,
        ClaimedAt = e.ClaimedAt,
        CompletedAt = e.CompletedAt,
        CreatedAt = e.CreatedAt,
    };
}
