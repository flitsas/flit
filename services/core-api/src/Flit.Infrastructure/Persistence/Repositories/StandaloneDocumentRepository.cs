using Flit.Admin.Domain.GeneracionDocumental;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio de <c>admin.standalone_documents</c> (Feature #12201,
/// ADR-0056-generacion-documental-standalone).
///
/// <para><b>Todas</b> las consultas filtran por <c>tenant_id</c> y por <c>deleted_at IS NULL</c>.
/// Ese filtro —junto con el ownership check del endpoint— es el aislamiento EFECTIVO entre
/// compañías: la política RLS de la tabla no aísla nada hoy (el repo no declara
/// <c>FORCE ROW LEVEL SECURITY</c> y la aplicación conecta como owner, que bypassa las policies).
/// Ningún test de aislamiento debe apoyarse en ella.</para>
///
/// <para><b>Por qué updates por columna y no <c>SaveChanges</c> de la entidad completa:</b> el
/// trigger <c>tr_standalone_documents_immutable</c> compara NEW contra OLD columna por columna. Un
/// UPDATE que reenvíe el snapshot ya escrito —aunque sea con el mismo valor semántico— puede
/// dispararlo; <c>ExecuteUpdateAsync</c> emite exactamente las columnas que cambian.</para>
/// </summary>
internal sealed class StandaloneDocumentRepository : IStandaloneDocumentRepository
{
    private readonly FlitDbContext _context;

    public StandaloneDocumentRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<StandaloneDocument?> FindByIdempotencyKeyAsync(
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

    public async Task<StandaloneDocument?> GetByIdAsync(
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
        StandaloneDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var entity = new StandaloneDocumentEntity
        {
            Id = document.Id == Guid.Empty ? Guid.CreateVersion7() : document.Id,
            TenantId = document.TenantId,
            CreatedByUserId = document.CreatedByUserId,
            DocumentType = document.DocumentType,
            Scenario = document.Scenario,
            Status = document.Status,
            ErrorCode = document.ErrorCode,
            ErrorField = document.ErrorField,
            StoragePath = document.StoragePath,
            StorageSha256 = document.StorageSha256,
            SizeBytes = document.SizeBytes,
            Filename = document.Filename,
            IdempotencyKey = document.IdempotencyKey,
            InputSummary = string.IsNullOrWhiteSpace(document.InputSummary) ? "{}" : document.InputSummary,
            RuesSnapshot = document.RuesSnapshot,
            DocumentSnapshot = document.DocumentSnapshot,
            DownloadedAt = null,
            DownloadCount = 0,
            CreatedAt = document.CreatedAt == default ? DateTimeOffset.UtcNow : document.CreatedAt,
            CreatedBy = document.CreatedByUserId,
        };

        _context.StandaloneDocuments.Add(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // La entidad recién insertada queda rastreada; los updates posteriores van por
        // ExecuteUpdateAsync (SQL directo) y no deben chocar con esta copia en el ChangeTracker.
        _context.Entry(entity).State = EntityState.Detached;
    }

    public Task SaveRuesSnapshotAsync(
        Guid tenantId,
        Guid id,
        string ruesSnapshotJson,
        CancellationToken cancellationToken = default)
        => Scoped(tenantId)
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.RuesSnapshot, ruesSnapshotJson)
                    .SetProperty(x => x.Status, StandaloneDocumentStatus.Processing)
                    .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);

    public Task MarkGeneratedAsync(
        Guid tenantId,
        Guid id,
        StandaloneDocumentFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        return Scoped(tenantId)
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.Status, StandaloneDocumentStatus.Generated)
                    .SetProperty(x => x.StoragePath, file.StoragePath)
                    .SetProperty(x => x.StorageSha256, file.StorageSha256)
                    .SetProperty(x => x.SizeBytes, file.SizeBytes)
                    .SetProperty(x => x.Filename, file.Filename)
                    .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
    }

    public Task MarkErrorAsync(
        Guid tenantId,
        Guid id,
        string errorCode,
        string? errorField = null,
        CancellationToken cancellationToken = default)
    {
        // El CHECK ck_standalone_documents_error_con_codigo prohíbe un 'error' sin código: un estado
        // inauditable obligaría a mirar logs para saber qué pasó.
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        return Scoped(tenantId)
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.Status, StandaloneDocumentStatus.Error)
                    .SetProperty(x => x.ErrorCode, errorCode)
                    .SetProperty(x => x.ErrorField, errorField)
                    .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
    }

    /// <summary>Universo visible: SIEMPRE el tenant indicado y solo filas no borradas lógicamente.</summary>
    private IQueryable<StandaloneDocumentEntity> Scoped(Guid tenantId) =>
        _context.StandaloneDocuments.Where(x => x.TenantId == tenantId && x.DeletedAt == null);

    private static StandaloneDocument Map(StandaloneDocumentEntity e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        CreatedByUserId = e.CreatedByUserId,
        DocumentType = e.DocumentType,
        Scenario = e.Scenario,
        Status = e.Status,
        ErrorCode = e.ErrorCode,
        ErrorField = e.ErrorField,
        StoragePath = e.StoragePath,
        StorageSha256 = e.StorageSha256,
        SizeBytes = e.SizeBytes,
        Filename = e.Filename,
        IdempotencyKey = e.IdempotencyKey,
        InputSummary = e.InputSummary,
        RuesSnapshot = e.RuesSnapshot,
        DocumentSnapshot = e.DocumentSnapshot,
        DownloadedAt = e.DownloadedAt,
        DownloadCount = e.DownloadCount,
        CreatedAt = e.CreatedAt,
    };
}
