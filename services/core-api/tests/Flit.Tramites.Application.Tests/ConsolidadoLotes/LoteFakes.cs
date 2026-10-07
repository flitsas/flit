using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13371 — dobles mínimos para los tests del lote: merger que concatena y storage en memoria que
/// registra subidas y borrados. Uso de ejemplo: <c>new GenerarConsolidadoHandler(repo, new LoteFakeMerger(), new LoteFakeStorage())</c>.
/// </summary>
internal sealed class LoteFakeMerger : IExpedienteConsolidadoMerger
{
    public byte[] NormalizeToPdf(byte[] content, string mimetype) => content;

    public byte[] Merge(IReadOnlyList<byte[]> pdfParts) => pdfParts.SelectMany(x => x).ToArray();

    public byte[] Compose(MergeRequest request) => Merge(request.Parts.Select(p => p.Pdf).ToList());
}

/// <inheritdoc cref="LoteFakeMerger"/>
internal sealed class LoteFakeStorage : IAttachmentStorage
{
    public List<string> Saved { get; } = [];
    public List<string> Deleted { get; } = [];
    public Dictionary<string, byte[]> Files { get; } = new();

    /// <summary>Simula la caída del almacenamiento al subir.</summary>
    public bool FallarAlGuardar { get; set; }

    public async Task<StoredFile> SaveAsync(
        Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
    {
        if (FallarAlGuardar)
            throw new IOException("almacenamiento caído (simulado)");

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        var path = $"{procedureInstanceId:D}/{tipo}_saved_{Saved.Count}";
        Files[path] = bytes;
        Saved.Add(path);
        return new StoredFile(path, $"sha-{tipo}-nuevo-{Saved.Count}", bytes.Length);
    }

    public Task<PresignedUpload> CreatePresignedUploadAsync(
        Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public void Delete(string storagePath)
    {
        Deleted.Add(storagePath);
        Files.Remove(storagePath);
    }

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
        Task.FromResult<Stream?>(Files.TryGetValue(storagePath, out var bytes) ? new MemoryStream(bytes) : null);

    public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
        string storagePath, CancellationToken ct = default) =>
        Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
}
