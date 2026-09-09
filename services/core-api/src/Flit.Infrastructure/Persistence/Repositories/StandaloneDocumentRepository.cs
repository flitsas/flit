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
            // I3 — vínculo con el lote. Va en el INSERT y nunca en un UPDATE: la capa 2 del trigger
            // (DDL 106) congela batch_id y row_number, incluido el paso de nulo a valor.
            BatchId = document.BatchId,
            RowNumber = document.RowNumber,
            ValidationErrors = string.IsNullOrWhiteSpace(document.ValidationErrors)
                ? "[]"
                : document.ValidationErrors,
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
    /// Filas ya materializadas del lote (Feature #12201, I3). El worker las consulta antes de
    /// procesar para no rehacer lo hecho cuando el reaper devuelve un lote atascado (R5). Es una
    /// optimización: el control duro sigue siendo el índice único
    /// <c>uq_standalone_documents_batch_row</c>.
    /// </summary>
    public async Task<IReadOnlyList<StandaloneDocumentBatchRowState>> ListBatchRowStatesAsync(
        Guid tenantId,
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        var filas = await Scoped(tenantId)
            .Where(x => x.BatchId == batchId && x.RowNumber != null)
            .Select(x => new { Numero = x.RowNumber!.Value, x.Status })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. filas.Select(f => new StandaloneDocumentBatchRowState(f.Numero, f.Status))];
    }

    /// <summary>
    /// Filas de un lote para el seguimiento (HU #12211), ordenadas por número de fila ascendente.
    /// La proyección se arma en SQL y <b>no selecciona snapshots ni <c>storage_path</c></b>.
    /// </summary>
    public async Task<StandaloneDocumentBatchItemsPage> ListBatchItemsAsync(
        Guid tenantId,
        Guid batchId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var pagina = page < 1 ? 1 : page;
        var tamano = pageSize switch
        {
            < 1 => 20,
            > 100 => 100,
            _ => pageSize,
        };

        var query = Scoped(tenantId).Where(x => x.BatchId == batchId);

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderBy(x => x.RowNumber)
            .ThenBy(x => x.Id)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .Select(x => new StandaloneDocumentBatchItem
            {
                Id = x.Id,
                RowNumber = x.RowNumber,
                DocumentType = x.DocumentType,
                Scenario = x.Scenario,
                Status = x.Status,
                ErrorCode = x.ErrorCode,
                ErrorField = x.ErrorField,
                ValidationErrors = x.ValidationErrors,
                Filename = x.Filename,
                CreatedAt = x.CreatedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new StandaloneDocumentBatchItemsPage(items, pagina, tamano, total);
    }

    /// <summary>
    /// Entradas del ZIP del lote (CF-15). El <c>WHERE status = 'generated'</c> va aquí, en SQL, y no
    /// en el streamer: así «solo los generados entran al ZIP» es una propiedad de la consulta y no
    /// un filtro que alguien pueda olvidar al armar el archivo. Se descartan además las filas sin
    /// binario, que el CHECK <c>ck_standalone_documents_generated_completo</c> ya hace imposibles.
    /// <para>Apoyada en el índice parcial <c>ix_standalone_documents_batch_status</c>.</para>
    /// </summary>
    public async Task<IReadOnlyList<StandaloneDocumentBatchZipEntry>> ListBatchGeneratedFilesAsync(
        Guid tenantId,
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        var filas = await Scoped(tenantId)
            .Where(x => x.BatchId == batchId
                && x.Status == StandaloneDocumentStatus.Generated
                && x.StoragePath != null
                && x.Filename != null)
            .OrderBy(x => x.RowNumber)
            .ThenBy(x => x.Id)
            .Select(x => new { x.RowNumber, Filename = x.Filename!, StoragePath = x.StoragePath! })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. filas.Select(f => new StandaloneDocumentBatchZipEntry(f.RowNumber, f.Filename, f.StoragePath))];
    }

    /// <summary>
    /// Detalle de errores de una fila del lote (CF-13). Columna EXENTA del trigger de inmutabilidad
    /// —como <c>downloaded_at</c>—, así que se puede escribir después de que el handler haya cerrado
    /// la fila en <c>error</c>. Tocar aquí cualquier columna congelada haría fallar el UPDATE con
    /// <c>check_violation</c>.
    /// </summary>
    public Task SaveValidationErrorsAsync(
        Guid tenantId,
        Guid id,
        string validationErrorsJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(validationErrorsJson);

        return Scoped(tenantId)
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.ValidationErrors, validationErrorsJson)
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

        // El ÚNICO punto del repositorio que puede producir una consulta sin `WHERE tenant_id`, y
        // solo cuando el filtro trae `null` a conciencia. `ListStandaloneDocumentsHandler` es quien
        // lo decide, y únicamente tras comprobar que quien pregunta es SuperAdmin y lo pidió
        // explícitamente. El borrado lógico se sigue respetando en ambos caminos.
        var query = filter.TenantId is { } tenant
            ? Scoped(tenant)
            : _context.StandaloneDocuments.Where(x => x.DeletedAt == null);

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

        // CF-18 en I3: el filtro por lote es un WHERE mas, con AND sobre los anteriores. No excluye
        // ni sustituye a los de tipo, fecha, usuario o estado.
        if (filter.BatchId is { } lote)
        {
            query = query.Where(x => x.BatchId == lote);
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
        BatchId = e.BatchId,
        RowNumber = e.RowNumber,
        ValidationErrors = e.ValidationErrors,
    };
}
