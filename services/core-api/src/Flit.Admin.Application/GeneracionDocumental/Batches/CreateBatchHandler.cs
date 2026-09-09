using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Application.GeneracionDocumental.Batches;

/// <summary>
/// Orden de carga de un lote XLSX (CF-11). El tenant y el autor salen del JWT, no del cuerpo.
/// </summary>
public sealed record CreateBatchCommand(
    Guid TenantId,
    Guid UserId,
    string? Filename,
    Stream? Content,
    string? IdempotencyKey = null);

/// <summary>Desenlaces de la carga. El endpoint los traduce a códigos HTTP.</summary>
public enum CreateBatchOutcome
{
    /// <summary>Lote encolado. 202.</summary>
    Accepted = 0,

    /// <summary>Replay de idempotencia: es el lote que ya existía (CF-16). 200.</summary>
    AlreadyExists = 1,

    /// <summary>Sin archivo o sin nombre. 400 — no se persiste nada.</summary>
    InvalidRequest = 2,

    /// <summary>
    /// El archivo se rechaza entero: no es XLSX, el encabezado no es el de la v1 o trae más de 100
    /// filas. 422 — <b>y el archivo NO se persiste en storage</b>.
    /// </summary>
    Rejected = 3,
}

public sealed record CreateBatchResult(
    CreateBatchOutcome Outcome,
    Guid? BatchId,
    string? Status,
    int Total,
    string? ErrorCode);

/// <summary>
/// Recibe el XLSX de carga masiva, lo valida y encola el lote (CF-11/CF-16, Feature #12201 I3).
///
/// <para><b>El orden de las operaciones es el contrato, no una preferencia:</b></para>
/// <list type="number">
/// <item>idempotencia primero — repetir la clave no puede escribir un archivo fuente nuevo (CF-16);</item>
/// <item>parseo y validación después — un archivo que no es XLSX, con encabezado ajeno a la v1 o con
/// 101 filas <b>no llega a storage</b>: se responde 422 y no queda basura;</item>
/// <item>storage y cabecera al final, ya con el total de filas conocido.</item>
/// </list>
///
/// <para><b>Aquí NO se crean las filas del lote.</b> Las crea el worker, una por una, al generarlas:
/// es lo que hace del índice único <c>uq_standalone_documents_batch_row</c> el control real de
/// «ninguna fila se genera dos veces» cuando el reaper devuelve un lote atascado (R5). Pre-crearlas
/// aquí dejaría ese índice sin función y obligaría a copiar el contenido del XLSX —PII completa de
/// las partes— a una columna, cuando el archivo ya está en storage y el worker puede releerlo.</para>
/// </summary>
public sealed class CreateBatchHandler
{
    /// <summary>Agrupación del XLSX fuente en storage. Distinta de la de los PDF generados.</summary>
    internal const string StorageTipo = "generacion_documental_lote";

    private readonly IStandaloneDocumentBatchRepository _batches;
    private readonly IStandaloneDocumentXlsxParser _parser;
    private readonly IStandaloneDocumentStorage _storage;
    private readonly TimeProvider _timeProvider;

    public CreateBatchHandler(
        IStandaloneDocumentBatchRepository batches,
        IStandaloneDocumentXlsxParser parser,
        IStandaloneDocumentStorage storage,
        TimeProvider? timeProvider = null)
    {
        _batches = batches ?? throw new ArgumentNullException(nameof(batches));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CreateBatchResult> HandleAsync(
        CreateBatchCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Content is null || string.IsNullOrWhiteSpace(command.Filename))
        {
            return new CreateBatchResult(CreateBatchOutcome.InvalidRequest, null, null, 0, "invalid_request");
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? null
            : command.IdempotencyKey.Trim();

        if (idempotencyKey is not null)
        {
            var existing = await _batches
                .FindByIdempotencyKeyAsync(command.TenantId, idempotencyKey, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                return new CreateBatchResult(
                    CreateBatchOutcome.AlreadyExists,
                    existing.Id,
                    existing.Status,
                    existing.TotalItems,
                    null);
            }
        }

        // El parser NO lanza ante un binario ajeno: un archivo del usuario no es una excepción del
        // servidor. Devuelve el código y aquí se traduce a 422 sin tocar storage.
        var parsed = _parser.Parse(command.Content);
        if (parsed.ErrorCode is not null)
        {
            return new CreateBatchResult(CreateBatchOutcome.Rejected, null, null, 0, parsed.ErrorCode);
        }

        // El XLSX fuente va a storage (PROHIBIDO BYTEA, igual que los PDF). El worker lo relee.
        command.Content.Position = 0;
        var stored = await _storage
            .SaveAsync(command.TenantId, StorageTipo, command.Filename, command.Content, cancellationToken)
            .ConfigureAwait(false);

        var batch = new StandaloneDocumentBatch
        {
            Id = Guid.CreateVersion7(),
            TenantId = command.TenantId,
            CreatedByUserId = command.UserId,
            Status = StandaloneDocumentBatchStatus.Queued,
            TemplateVersion = StandaloneBatchTemplate.Version,
            SourceFilename = command.Filename,
            SourceStoragePath = stored.StoragePath,
            SourceSha256 = stored.Sha256,
            TotalItems = parsed.Rows.Count,
            GeneratedCount = 0,
            ErrorCount = 0,
            IdempotencyKey = idempotencyKey,
            CreatedAt = _timeProvider.GetUtcNow(),
        };

        await _batches.InsertAsync(batch, cancellationToken).ConfigureAwait(false);

        return new CreateBatchResult(
            CreateBatchOutcome.Accepted,
            batch.Id,
            StandaloneDocumentBatchStatus.Queued,
            batch.TotalItems,
            null);
    }
}
