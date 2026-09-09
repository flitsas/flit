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

    public Task SaveDocumentSnapshotAsync(
        Guid tenantId,
        Guid id,
        string documentSnapshotJson,
        CancellationToken cancellationToken = default)
        => Scoped(tenantId)
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.DocumentSnapshot, documentSnapshotJson)
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

    /// <summary>
    /// Historial paginado (CF-17/CF-18). La proyección se arma en SQL y <b>no selecciona
    /// <c>document_snapshot</c>, <c>rues_snapshot</c> ni <c>storage_path</c></b>: el snapshot es PII
    /// alta y no puede salir en un listado, ni siquiera «por si acaso» dentro de la entidad.
    /// </summary>
    public async Task<StandaloneDocumentPage> ListAsync(
        StandaloneDocumentFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize switch
        {
            < 1 => 20,
            > 100 => 100,
            _ => filter.PageSize,
        };

        var query = Scoped(filter.TenantId);

        if (!string.IsNullOrWhiteSpace(filter.DocumentType))
        {
            var documentType = filter.DocumentType;
            query = query.Where(x => x.DocumentType == documentType);
        }

        if (filter.Statuses is { Count: > 0 })
        {
            // Estados INTERNOS: «En proceso» llega ya expandido a pending + processing (CF-21).
            var statuses = filter.Statuses.ToArray();
            query = query.Where(x => statuses.Contains(x.Status));
        }

        if (filter.DateFrom is { } desde)
        {
            query = query.Where(x => x.CreatedAt >= desde);
        }

        if (filter.DateTo is { } hasta)
        {
            query = query.Where(x => x.CreatedAt < hasta);
        }

        if (filter.CreatedByUserId is { } autor)
        {
            query = query.Where(x => x.CreatedByUserId == autor);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new StandaloneDocumentListItem
            {
                Id = x.Id,
                DocumentType = x.DocumentType,
                Scenario = x.Scenario,
                Status = x.Status,
                ErrorCode = x.ErrorCode,
                Filename = x.Filename,
                CompanyName = _context.Tenants
                    .Where(t => t.Id == x.TenantId)
                    .Select(t => t.LegalName)
                    .FirstOrDefault(),
                CreatedByUserId = x.CreatedByUserId,
                CreatedByUserName = _context.Users
                    .Where(u => u.Id == x.CreatedByUserId)
                    .Select(u => u.DisplayName)
                    .FirstOrDefault(),
                CreatedAt = x.CreatedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new StandaloneDocumentPage(items, page, pageSize, total);
    }

    /// <summary>
    /// Auditoría de descarga (CF-19) en un ÚNICO UPDATE atómico. El contador se incrementa con una
    /// expresión sobre la propia columna —<c>download_count = download_count + 1</c> en SQL—, de
    /// modo que dos descargas dejan 2 aunque ocurran a la vez: leer en el handler y escribir después
    /// perdería una.
    /// <para>Las tres columnas escritas están exentas del trigger de inmutabilidad. Añadir aquí
    /// cualquier columna congelada (snapshot, storage_path, status…) haría fallar toda descarga de
    /// un documento ya generado con <c>check_violation</c>.</para>
    /// </summary>
    public Task<int> RegisterDownloadAsync(
        Guid tenantId,
        Guid id,
        DateTimeOffset downloadedAt,
        CancellationToken cancellationToken = default)
        => Scoped(tenantId)
            .Where(x => x.Id == id && x.Status == StandaloneDocumentStatus.Generated)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.DownloadCount, x => x.DownloadCount + 1)
                    .SetProperty(x => x.DownloadedAt, downloadedAt)
                    .SetProperty(x => x.UpdatedAt, downloadedAt),
                cancellationToken);

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
