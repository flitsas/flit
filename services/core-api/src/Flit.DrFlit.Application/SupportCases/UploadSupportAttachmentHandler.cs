using Flit.DrFlit.Application.Abstractions;

namespace Flit.DrFlit.Application.SupportCases;

public sealed record UploadSupportAttachmentCommand(
    Guid TenantId,
    Guid UserId,
    string? FileName,
    string? DeclaredContentType,
    long DeclaredLength,
    Stream? Content);

public enum UploadSupportAttachmentOutcome
{
    Uploaded,
    MissingFile,
    InvalidType,
    TooLarge,
}

public sealed record UploadSupportAttachmentResult(
    UploadSupportAttachmentOutcome Outcome,
    DrFlitStoredAttachment? Attachment = null,
    string? Error = null);

/// <summary>
/// Sube un adjunto de un caso de soporte todavía sin confirmar (HU #12924). Valida en tres capas, porque
/// el nombre y el tipo declarado los controla el cliente:
/// <list type="number">
///   <item>la extensión debe corresponder a un tipo permitido por la configuración;</item>
///   <item>el tipo declarado no puede contradecir la extensión;</item>
///   <item>los primeros bytes deben ser del formato que dice la extensión (un .pdf que es un .exe no pasa).</item>
/// </list>
/// El límite de CANTIDAD no se aplica aquí (AC3): lo aplica el formulario y se revalida al confirmar.
/// </summary>
public sealed class UploadSupportAttachmentHandler(
    IDrFlitSupportAttachmentStore store,
    IDrFlitSupportCaseSettings settings,
    TimeProvider clock)
{
    private static readonly char[] PathSeparators = ['/', '\\'];

    /// <summary>Extensión → tipo MIME. Solo se aceptan las que además estén en la configuración.</summary>
    private static readonly Dictionary<string, string> MimeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
    };

    public async Task<UploadSupportAttachmentResult> HandleAsync(UploadSupportAttachmentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Content is null || command.DeclaredLength <= 0 || string.IsNullOrWhiteSpace(command.FileName))
            return new(UploadSupportAttachmentOutcome.MissingFile, Error: "Falta el archivo.");

        // Path.GetFileName solo corta el separador nativo: en el servidor Linux un nombre venido de un
        // navegador Windows ("C:\temp\captura.png") pasaría completo. Se cortan ambos separadores a mano.
        var trimmedName = command.FileName.Trim();
        var fileName = trimmedName[(trimmedName.LastIndexOfAny(PathSeparators) + 1)..];
        var extension = Path.GetExtension(fileName);
        if (!MimeByExtension.TryGetValue(extension, out var mime)
            || !settings.AllowedMimeTypes.Contains(mime, StringComparer.OrdinalIgnoreCase))
        {
            return new(UploadSupportAttachmentOutcome.InvalidType, Error: "Tipo de archivo no permitido.");
        }

        var declared = command.DeclaredContentType?.Split(';')[0].Trim();
        if (!string.IsNullOrEmpty(declared)
            && !string.Equals(declared, "application/octet-stream", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(declared, mime, StringComparison.OrdinalIgnoreCase))
        {
            return new(UploadSupportAttachmentOutcome.InvalidType, Error: "El tipo del archivo no coincide con su extensión.");
        }

        if (command.DeclaredLength > settings.MaxFileSizeBytes)
            return new(UploadSupportAttachmentOutcome.TooLarge, Error: $"El archivo supera {settings.MaxFileSizeBytes / (1024 * 1024)} MB.");

        // Se lee hasta tope + 1 byte: si el cliente mintió en el tamaño declarado, se detecta aquí.
        using var buffer = new MemoryStream();
        await CopyUpToAsync(command.Content, buffer, settings.MaxFileSizeBytes + 1, ct).ConfigureAwait(false);
        if (buffer.Length == 0)
            return new(UploadSupportAttachmentOutcome.MissingFile, Error: "El archivo está vacío.");
        if (buffer.Length > settings.MaxFileSizeBytes)
            return new(UploadSupportAttachmentOutcome.TooLarge, Error: $"El archivo supera {settings.MaxFileSizeBytes / (1024 * 1024)} MB.");
        if (!ContentMatches(mime, buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 512))))
            return new(UploadSupportAttachmentOutcome.InvalidType, Error: "El contenido del archivo no corresponde a su tipo.");

        var now = clock.GetUtcNow();
        await store.PurgeExpiredAsync(command.TenantId, command.UserId, now, ct).ConfigureAwait(false);

        buffer.Position = 0;
        var stored = await store.SaveAsync(
            command.TenantId, command.UserId, fileName, mime, buffer, now + settings.AttachmentTtl, ct).ConfigureAwait(false);

        return new(UploadSupportAttachmentOutcome.Uploaded, stored);
    }

    private static async Task CopyUpToAsync(Stream source, Stream destination, long limit, CancellationToken ct)
    {
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while (total < limit && (read = await source.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - total)), ct).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
            total += read;
        }
    }

    /// <summary>Firma de los primeros bytes. Texto plano: sin bytes nulos en la cabecera.</summary>
    internal static bool ContentMatches(string mime, ReadOnlySpan<byte> head) => mime switch
    {
        "image/png" => head.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        "image/jpeg" => head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]),
        "image/webp" => head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8),
        "application/pdf" => head.StartsWith("%PDF-"u8),
        "text/plain" => !head.Contains((byte)0),
        _ => false,
    };
}
