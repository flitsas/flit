using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// Doble en memoria de <c>admin.standalone_document_batches</c> (HU #12210). Escrito a mano y no con
/// NSubstitute porque los AC no se juegan en «se llamó al método», sino en <b>cuántos lotes</b>
/// existen tras un replay de idempotencia y en <b>con qué contadores</b> se cerró el lote.
///
/// <para>Imita también el claim: <see cref="ClaimNextAsync"/> toma encolados y recupera los
/// <c>processing</c> vencidos (reaper R5), igual que el UPDATE condicional del repositorio real.</para>
/// </summary>
internal sealed class FakeStandaloneDocumentBatchRepository : IStandaloneDocumentBatchRepository
{
    public List<StandaloneDocumentBatch> Rows { get; } = [];

    /// <summary>Cierres registrados, en orden: estado terminal y contadores.</summary>
    public List<(Guid Id, string Status, int Generated, int Errors)> Completions { get; } = [];

    public Task<StandaloneDocumentBatch?> FindByIdempotencyKeyAsync(
        Guid tenantId, string idempotencyKey, CancellationToken cancellationToken = default)
        => Task.FromResult(Rows.FirstOrDefault(
            b => b.TenantId == tenantId && b.IdempotencyKey == idempotencyKey));

    public Task<StandaloneDocumentBatch?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Rows.FirstOrDefault(b => b.TenantId == tenantId && b.Id == id));

    public Task InsertAsync(StandaloneDocumentBatch batch, CancellationToken cancellationToken = default)
    {
        Rows.Add(batch);
        return Task.CompletedTask;
    }

    public Task<StandaloneDocumentBatch?> ClaimNextAsync(
        DateTimeOffset now, DateTimeOffset reclaimBefore, CancellationToken cancellationToken = default)
    {
        var index = Rows.FindIndex(b =>
            b.Status == StandaloneDocumentBatchStatus.Queued
            || (b.Status == StandaloneDocumentBatchStatus.Processing
                && b.ClaimedAt is { } tomado
                && tomado < reclaimBefore));

        if (index < 0)
        {
            return Task.FromResult<StandaloneDocumentBatch?>(null);
        }

        var reclamado = Copiar(
            Rows[index],
            status: StandaloneDocumentBatchStatus.Processing,
            claimedAt: now);

        Rows[index] = reclamado;
        return Task.FromResult<StandaloneDocumentBatch?>(reclamado);
    }

    public Task CompleteAsync(
        Guid id,
        string status,
        int generatedCount,
        int errorCount,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        Completions.Add((id, status, generatedCount, errorCount));

        var index = Rows.FindIndex(b => b.Id == id);
        if (index >= 0)
        {
            Rows[index] = Copiar(
                Rows[index],
                status: status,
                generated: generatedCount,
                errors: errorCount,
                completedAt: completedAt);
        }

        return Task.CompletedTask;
    }

    private static StandaloneDocumentBatch Copiar(
        StandaloneDocumentBatch b,
        string? status = null,
        int? generated = null,
        int? errors = null,
        DateTimeOffset? claimedAt = null,
        DateTimeOffset? completedAt = null) => new()
        {
            Id = b.Id,
            TenantId = b.TenantId,
            CreatedByUserId = b.CreatedByUserId,
            Status = status ?? b.Status,
            TemplateVersion = b.TemplateVersion,
            SourceFilename = b.SourceFilename,
            SourceStoragePath = b.SourceStoragePath,
            SourceSha256 = b.SourceSha256,
            TotalItems = b.TotalItems,
            GeneratedCount = generated ?? b.GeneratedCount,
            ErrorCount = errors ?? b.ErrorCount,
            IdempotencyKey = b.IdempotencyKey,
            ClaimedAt = claimedAt ?? b.ClaimedAt,
            CompletedAt = completedAt ?? b.CompletedAt,
            CreatedAt = b.CreatedAt,
        };
}
