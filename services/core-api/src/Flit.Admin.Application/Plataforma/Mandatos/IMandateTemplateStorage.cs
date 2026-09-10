namespace Flit.Admin.Application.Plataforma.Mandatos;

/// <summary>Resultado de persistir el PDF blank de plantilla propia.</summary>
public sealed record MandateTemplateStoredFile(string StoragePath, string Sha256, long SizeBytes);

/// <summary>Custodia del PDF blank de mandato propio por OT (sin tenant; agrupado por officeId).</summary>
public interface IMandateTemplateStorage
{
    Task<MandateTemplateStoredFile> SavePdfAsync(
        Guid transitOfficeId,
        string fileName,
        Stream content,
        CancellationToken ct = default);

    Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default);

    void Delete(string? storagePath);
}
