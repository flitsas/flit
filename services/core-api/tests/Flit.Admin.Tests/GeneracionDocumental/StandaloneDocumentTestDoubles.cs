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
    DateTimeOffset CreatedAt)
{
    public static StandaloneDocumentSnapshot From(StandaloneDocument d) => new(
        d.Id, d.TenantId, d.CreatedByUserId, d.DocumentType, d.Scenario, d.Status, d.ErrorCode,
        d.ErrorField, d.StoragePath, d.StorageSha256, d.SizeBytes, d.Filename, d.IdempotencyKey,
        d.InputSummary, d.RuesSnapshot, d.CreatedAt);

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
        CreatedAt = CreatedAt,
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

    public Task<StoredStandaloneDocument> SaveAsync(
        Guid tenantId, string tipo, string filename, Stream content,
        CancellationToken cancellationToken = default)
    {
        Saved.Add((tenantId, tipo, filename, content.Length));
        return Task.FromResult(new StoredStandaloneDocument(
            $"fm://{tenantId}/{filename}",
            "9f2b1c0a5d4e6f7a8b9c0d1e2f3a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c",
            content.Length));
    }
}
