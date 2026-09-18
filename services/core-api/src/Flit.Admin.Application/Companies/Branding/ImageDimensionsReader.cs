namespace Flit.Admin.Application.Companies.Branding;

/// <summary>
/// Lee el ancho/alto de PNG, JPEG y WebP a partir de sus cabeceras, sin decodificar el binario
/// completo ni depender de una librería de imágenes (HU #12412 AC7: <c>admin.tenant_brand_logos</c>
/// exige <c>width_px &gt; 0 AND height_px &gt; 0</c>). Los límites de tamaño mínimo/máximo los aplica
/// #12413 (<see cref="IBrandAssetValidator"/> permisivo hoy); esta clase solo mide.
/// <para>Requiere un stream <c>seekable</c> (patrón <c>ImageContentTypeSniffer</c>): reposiciona al inicio tras leer.</para>
/// </summary>
public static class ImageDimensionsReader
{
    public static async Task<(int Width, int Height)> ReadAsync(
        Stream stream,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            return (0, 0);
        }

        var startPosition = stream.Position;
        var buffer = new byte[64];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
        stream.Position = startPosition;

        return contentType switch
        {
            "image/png" => ReadPng(buffer, read),
            "image/jpeg" => await ReadJpegAsync(stream, startPosition, cancellationToken).ConfigureAwait(false),
            "image/webp" => ReadWebp(buffer, read),
            _ => (0, 0),
        };
    }

    private static (int, int) ReadPng(byte[] h, int read)
    {
        // IHDR siempre empieza en el byte 16 (8 firma + 4 longitud + 4 "IHDR"): 4 bytes width, 4 bytes height, big-endian.
        if (read < 24)
        {
            return (0, 0);
        }

        var width = (h[16] << 24) | (h[17] << 16) | (h[18] << 8) | h[19];
        var height = (h[20] << 24) | (h[21] << 16) | (h[22] << 8) | h[23];
        return (width, height);
    }

    private static (int, int) ReadWebp(byte[] h, int read)
    {
        // VP8X (extendido) o VP8L/VP8 simple. Cubre el caso simple (VP8 lossy), suficiente para un logo.
        if (read < 30 || h[12] != 0x56 || h[13] != 0x50 || h[14] != 0x38)
        {
            return (0, 0);
        }

        if (h[15] == 0x20) // "VP8 " (lossy) — width/height en 2 bytes little-endian con 14 bits útiles, offset 26/28.
        {
            var width = ((h[27] << 8) | h[26]) & 0x3FFF;
            var height = ((h[29] << 8) | h[28]) & 0x3FFF;
            return (width, height);
        }

        return (0, 0);
    }

    private static async Task<(int, int)> ReadJpegAsync(Stream stream, long startPosition, CancellationToken ct)
    {
        stream.Position = startPosition + 2; // SOI (0xFFD8)
        var marker = new byte[4];

        while (stream.Position < stream.Length - 4)
        {
            var read = await stream.ReadAsync(marker.AsMemory(0, 4), ct).ConfigureAwait(false);
            if (read < 4 || marker[0] != 0xFF)
            {
                break;
            }

            var segmentType = marker[1];
            var segmentLength = (marker[2] << 8) | marker[3];

            var isSof = segmentType is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC);
            if (isSof)
            {
                var sof = new byte[5];
                var sofRead = await stream.ReadAsync(sof.AsMemory(0, 5), ct).ConfigureAwait(false);
                stream.Position = startPosition;
                if (sofRead < 5)
                {
                    return (0, 0);
                }

                var height = (sof[1] << 8) | sof[2];
                var width = (sof[3] << 8) | sof[4];
                return (width, height);
            }

            stream.Position += segmentLength - 2;
        }

        stream.Position = startPosition;
        return (0, 0);
    }
}
