using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// Dobles en memoria de los tres puertos y del repositorio de <c>admin.standalone_documents</c>
/// (HU #12203, Feature #12201). Se escriben a mano —en vez de con NSubstitute— porque varios AC no
/// se juegan en «se llamó al método», sino en <b>cuántas veces</b> se consultó al proveedor y
/// <b>cuántos archivos</b> quedaron en storage: contadores explícitos hacen esas aserciones legibles.
/// </summary>
internal sealed class FakeStandaloneDocumentRepository : IStandaloneDocumentRepository
{
    public List<StandaloneDocument> Rows { get; } = [];

    public List<(Guid Id, string Snapshot)> Snapshots { get; } = [];

    public List<(Guid Id, StandaloneDocumentFile File)> Generated { get; } = [];

    public List<(Guid Id, string ErrorCode, string? ErrorField)> Errors { get; } = [];

    /// <summary>Auditorías de descarga registradas (CF-19), en orden.</summary>
    public List<(Guid Id, DateTimeOffset At)> Downloads { get; } = [];

    public Task<StandaloneDocument?> FindByIdempotencyKeyAsync(
        Guid tenantId, string idempotencyKey, CancellationToken cancellationToken = default)
        => Task.FromResult(Rows.FirstOrDefault(
            r => r.TenantId == tenantId && r.IdempotencyKey == idempotencyKey));

    public Task<StandaloneDocument?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Rows.FirstOrDefault(r => r.TenantId == tenantId && r.Id == id));

    public Task InsertAsync(StandaloneDocument document, CancellationToken cancellationToken = default)
    {
        Rows.Add(document);
        return Task.CompletedTask;
    }

    public Task SaveRuesSnapshotAsync(
        Guid tenantId, Guid id, string ruesSnapshotJson, CancellationToken cancellationToken = default)
    {
        Snapshots.Add((id, ruesSnapshotJson));
        Replace(id, r => r with { Status = StandaloneDocumentStatus.Processing, RuesSnapshot = ruesSnapshotJson });
        return Task.CompletedTask;
    }

    /// <summary>Snapshots completos de transferencia escritos (CF-26), en orden.</summary>
    public List<(Guid Id, string Snapshot)> DocumentSnapshots { get; } = [];

    public Task SaveDocumentSnapshotAsync(
        Guid tenantId, Guid id, string documentSnapshotJson, CancellationToken cancellationToken = default)
    {
        DocumentSnapshots.Add((id, documentSnapshotJson));
        Replace(id, r => r with
        {
            Status = StandaloneDocumentStatus.Processing,
            DocumentSnapshot = documentSnapshotJson,
        });
        return Task.CompletedTask;
    }

    public Task MarkGeneratedAsync(
        Guid tenantId, Guid id, StandaloneDocumentFile file, CancellationToken cancellationToken = default)
    {
        Generated.Add((id, file));
        Replace(id, r => r with
        {
            Status = StandaloneDocumentStatus.Generated,
            StoragePath = file.StoragePath,
            StorageSha256 = file.StorageSha256,
            SizeBytes = file.SizeBytes,
            Filename = file.Filename,
        });
        return Task.CompletedTask;
    }

    /// <summary>Errores por fila escritos con <c>SaveValidationErrorsAsync</c> (CF-13, lotes).</summary>
    public List<(Guid Id, string Json)> ValidationErrors { get; } = [];

    public Task<IReadOnlyList<StandaloneDocumentBatchRowState>> ListBatchRowStatesAsync(
        Guid tenantId, Guid batchId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<StandaloneDocumentBatchRowState>>(
            [.. Rows
                .Where(r => r.TenantId == tenantId && r.BatchId == batchId && r.RowNumber != null)
                .Select(r => new StandaloneDocumentBatchRowState(r.RowNumber!.Value, r.Status))]);

    public Task SaveValidationErrorsAsync(
        Guid tenantId, Guid id, string validationErrorsJson, CancellationToken cancellationToken = default)
    {
        ValidationErrors.Add((id, validationErrorsJson));
        Replace(id, r => r with { ValidationErrors = validationErrorsJson });
        return Task.CompletedTask;
    }

    public Task MarkErrorAsync(
        Guid tenantId, Guid id, string errorCode, string? errorField = null,
        CancellationToken cancellationToken = default)
    {
        Errors.Add((id, errorCode, errorField));
        Replace(id, r => r with
        {
            Status = StandaloneDocumentStatus.Error,
            ErrorCode = errorCode,
            ErrorField = errorField,
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Historial en memoria con los mismos filtros que el SQL del repositorio real. Devuelve la
    /// PROYECCIÓN pobre en PII: aunque la fila guardada tenga <c>document_snapshot</c>, el listado
    /// no tiene dónde ponerlo.
    /// </summary>
    public Task<StandaloneDocumentPage> ListAsync(
        StandaloneDocumentFilter filter, CancellationToken cancellationToken = default)
    {
        var query = Rows.Where(r => r.TenantId == filter.TenantId);

        if (!string.IsNullOrWhiteSpace(filter.DocumentType))
        {
            query = query.Where(r => r.DocumentType == filter.DocumentType);
        }

        if (filter.Statuses is { Count: > 0 })
        {
            query = query.Where(r => filter.Statuses.Contains(r.Status));
        }

        if (filter.DateFrom is { } desde)
        {
            query = query.Where(r => r.CreatedAt >= desde);
        }

        if (filter.DateTo is { } hasta)
        {
            query = query.Where(r => r.CreatedAt < hasta);
        }

        if (filter.CreatedByUserId is { } autor)
        {
            query = query.Where(r => r.CreatedByUserId == autor);
        }

        var ordered = query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).ToList();

        var items = ordered
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(r => new StandaloneDocumentListItem
            {
                Id = r.Id,
                DocumentType = r.DocumentType,
                Scenario = r.Scenario,
                Status = r.Status,
                ErrorCode = r.ErrorCode,
                Filename = r.Filename,
                CompanyName = $"COMPANIA {r.TenantId.ToString()[..8]}",
                CreatedByUserId = r.CreatedByUserId,
                CreatedByUserName = "Usuario de prueba",
                CreatedAt = r.CreatedAt,
            })
            .ToList();

        return Task.FromResult(new StandaloneDocumentPage(items, filter.Page, filter.PageSize, ordered.Count));
    }

    /// <summary>
    /// Imita el UPDATE atómico: incrementa sobre el valor de la propia fila y fija la fecha en el
    /// mismo paso. Devuelve las filas afectadas (0 si no es del tenant o no está generada), que es
    /// lo que devuelve <c>ExecuteUpdateAsync</c>.
    /// </summary>
    public Task<int> RegisterDownloadAsync(
        Guid tenantId, Guid id, DateTimeOffset downloadedAt, CancellationToken cancellationToken = default)
    {
        var index = Rows.FindIndex(
            r => r.TenantId == tenantId && r.Id == id && r.Status == StandaloneDocumentStatus.Generated);

        if (index < 0)
        {
            return Task.FromResult(0);
        }

        Downloads.Add((id, downloadedAt));
        Replace(id, r => r with { DownloadCount = r.DownloadCount + 1, DownloadedAt = downloadedAt });
        return Task.FromResult(1);
    }

    /// <summary>
    /// <see cref="StandaloneDocument"/> es una clase con <c>init</c>: se sustituye la fila entera
    /// para imitar el UPDATE, sin mutar propiedades de solo inicialización.
    /// </summary>
    private void Replace(Guid id, Func<StandaloneDocumentSnapshot, StandaloneDocumentSnapshot> update)
    {
        var index = Rows.FindIndex(r => r.Id == id);
        if (index < 0)
        {
            return;
        }

        var updated = update(StandaloneDocumentSnapshot.From(Rows[index]));
        Rows[index] = updated.ToDocument();
    }
}

/// <summary>Proyección mutable de la fila, solo para los dobles de prueba.</summary>
internal sealed record StandaloneDocumentSnapshot(
    Guid Id,
    Guid TenantId,
    Guid CreatedByUserId,
    string DocumentType,
    string? Scenario,
    string Status,
    string? ErrorCode,
    string? ErrorField,
    string? StoragePath,
    string? StorageSha256,
    long? SizeBytes,
    string? Filename,
    string? IdempotencyKey,
    string InputSummary,
    string? RuesSnapshot,
    string? DocumentSnapshot,
    DateTimeOffset? DownloadedAt,
    int DownloadCount,
    DateTimeOffset CreatedAt,
    Guid? BatchId,
    int? RowNumber,
    string ValidationErrors)
{
    public static StandaloneDocumentSnapshot From(StandaloneDocument d) => new(
        d.Id, d.TenantId, d.CreatedByUserId, d.DocumentType, d.Scenario, d.Status, d.ErrorCode,
        d.ErrorField, d.StoragePath, d.StorageSha256, d.SizeBytes, d.Filename, d.IdempotencyKey,
        d.InputSummary, d.RuesSnapshot, d.DocumentSnapshot, d.DownloadedAt, d.DownloadCount, d.CreatedAt,
        d.BatchId, d.RowNumber, d.ValidationErrors);

    public StandaloneDocument ToDocument() => new()
    {
        Id = Id,
        TenantId = TenantId,
        CreatedByUserId = CreatedByUserId,
        DocumentType = DocumentType,
        Scenario = Scenario,
        Status = Status,
        ErrorCode = ErrorCode,
        ErrorField = ErrorField,
        StoragePath = StoragePath,
        StorageSha256 = StorageSha256,
        SizeBytes = SizeBytes,
        Filename = Filename,
        IdempotencyKey = IdempotencyKey,
        InputSummary = InputSummary,
        RuesSnapshot = RuesSnapshot,
        DocumentSnapshot = DocumentSnapshot,
        DownloadedAt = DownloadedAt,
        DownloadCount = DownloadCount,
        CreatedAt = CreatedAt,
        BatchId = BatchId,
        RowNumber = RowNumber,
        ValidationErrors = ValidationErrors,
    };
}

internal sealed class FakeStandaloneRuesCompanyLookup : IStandaloneRuesCompanyLookup
{
    public int Calls { get; private set; }

    public StandaloneRuesLookupResult Result { get; set; } = new(
        true,
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["rues_razon_social"] = "EMPRESA DE PRUEBA SAS",
            ["rues_nit"] = "900123456",
            ["rues_estado"] = "ACTIVA",
        },
        DateTimeOffset.Parse("2026-09-09T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        null);

    public Task<StandaloneRuesLookupResult> ConsultAsync(
        Guid tenantId, string nit, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}

internal sealed class FakeStandaloneRuesCertificateRenderer : IStandaloneRuesCertificateRenderer
{
    public int Calls { get; private set; }

    public string? LastReferenceNumber { get; private set; }

    public RenderedStandaloneDocument Render(
        IReadOnlyDictionary<string, string?> ruesFields, string referenceNumber)
    {
        Calls++;
        LastReferenceNumber = referenceNumber;
        return new RenderedStandaloneDocument(
            $"certificado_rues_{referenceNumber}.pdf", "application/pdf", [0x25, 0x50, 0x44, 0x46]);
    }
}

internal sealed class FakeStandaloneDocumentStorage : IStandaloneDocumentStorage
{
    public List<(Guid TenantId, string Tipo, string Filename, long Length)> Saved { get; } = [];

    /// <summary>Presigned URLs entregadas, para contar cuántas veces se firmó de verdad.</summary>
    public List<string> Presigned { get; } = [];

    /// <summary>Simula un binario ausente en storage: el puerto devuelve null.</summary>
    public bool BinarioDisponible { get; set; } = true;

    public Task<StoredStandaloneDocument> SaveAsync(
        Guid tenantId, string tipo, string filename, Stream content,
        CancellationToken cancellationToken = default)
    {
        using var copia = new MemoryStream();
        content.CopyTo(copia);
        var bytes = copia.ToArray();

        Saved.Add((tenantId, tipo, filename, bytes.LongLength));

        var path = $"fm://{tenantId}/{tipo}/{Saved.Count}/{filename}";
        Contenidos[path] = bytes;

        return Task.FromResult(new StoredStandaloneDocument(
            path,
            "9f2b1c0a5d4e6f7a8b9c0d1e2f3a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c",
            bytes.LongLength));
    }

    /// <summary>Binarios guardados por ruta, para que el worker pueda releer el XLSX fuente.</summary>
    public Dictionary<string, byte[]> Contenidos { get; } = [];

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream?>(
            Contenidos.TryGetValue(storagePath, out var bytes)
                ? new MemoryStream(bytes, writable: false)
                : null);

    public Task<StandaloneDocumentDownloadLink?> GetPresignedViewUrlAsync(
        string storagePath, CancellationToken cancellationToken = default)
    {
        if (!BinarioDisponible)
        {
            return Task.FromResult<StandaloneDocumentDownloadLink?>(null);
        }

        Presigned.Add(storagePath);
        return Task.FromResult<StandaloneDocumentDownloadLink?>(new StandaloneDocumentDownloadLink(
            $"https://storage.example/{storagePath}?X-Amz-Signature=firma-{Presigned.Count}",
            DateTimeOffset.UtcNow.AddMinutes(10)));
    }
}
