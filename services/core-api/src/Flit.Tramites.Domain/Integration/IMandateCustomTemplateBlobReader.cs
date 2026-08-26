namespace Flit.Tramites.Domain.Integration;

/// <summary>Lee el PDF blank de plantilla propia de mandato por path de storage (sin tenant).</summary>
public interface IMandateCustomTemplateBlobReader
{
    Task<byte[]?> OpenPdfAsync(string storagePath, CancellationToken cancellationToken = default);
}

/// <summary>Default inerte: nunca encuentra plantilla propia.</summary>
public sealed class NullMandateCustomTemplateBlobReader : IMandateCustomTemplateBlobReader
{
    public static NullMandateCustomTemplateBlobReader Instance { get; } = new();

    public Task<byte[]?> OpenPdfAsync(string storagePath, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);
}
