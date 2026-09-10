namespace Flit.Admin.Application.Banners.GetBannerImage;

/// <summary>
/// Detecta el <c>Content-Type</c> de una imagen por sus primeros bytes (magic numbers).
/// <c>admin.banners</c> no persiste el content-type (solo <c>image_storage_path</c> +
/// <c>image_sha256</c>, ADR-0058); HU #12239 restringe la subida a PNG/JPEG/WEBP, asi que
/// alcanza con reconocer esos tres formatos.
///
/// <para>Requiere un stream <c>seekable</c> — se hace <c>Seek</c> de vuelta al inicio tras leer
/// la cabecera, para que el caller pueda transmitir el binario completo despues. Cumplido por
/// <c>FileManagerAttachmentStorage</c>, que devuelve un <see cref="MemoryStream"/>.</para>
/// </summary>
public static class ImageContentTypeSniffer
{
    private const string DefaultContentType = "application/octet-stream";

    public static async Task<string> DetectAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            return DefaultContentType;
        }

        var startPosition = stream.Position;
        var header = new byte[12];
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken)
            .ConfigureAwait(false);
        stream.Position = startPosition;

        if (IsPng(header, read)) return "image/png";
        if (IsJpeg(header, read)) return "image/jpeg";
        if (IsWebp(header, read)) return "image/webp";

        return DefaultContentType;
    }

    private static bool IsPng(byte[] h, int read) =>
        read >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47
            && h[4] == 0x0D && h[5] == 0x0A && h[6] == 0x1A && h[7] == 0x0A;

    private static bool IsJpeg(byte[] h, int read) =>
        read >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF;

    private static bool IsWebp(byte[] h, int read) =>
        read >= 12 && h[0] == 0x52 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x46
            && h[8] == 0x57 && h[9] == 0x45 && h[10] == 0x42 && h[11] == 0x50;
}
