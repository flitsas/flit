using Flit.Tramites.Application.Identity;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// HU #13371 (épica #13216) — única puerta de decodificación con ImageSharp. La versión 2.1.11 está
/// fijada por PdfSharpCore y tiene cinco avisos abiertos sin 2.x parcheada (GHSA-gwg2-r3hj-4w44,
/// GHSA-j3p4-wp97-rph4, GHSA-j9gm-c75j-xc9q, GHSA-wmxv-xphr-5c9g, GHSA-jjfr-hcj7-qf5w; varios en el
/// códec TIFF/BigTIFF). Para que esos códecs no sean alcanzables:
/// <list type="number">
/// <item>se exige la firma de archivo PNG o JPEG (<see cref="IdentitySignatureImageFormat.IsSupported"/>)
/// antes de tocar ImageSharp, también antes de <c>XImage.FromStream</c> de PdfSharpCore, que decodifica
/// con la configuración por defecto de ImageSharp (autodetección de TIFF incluida);</item>
/// <item>la propia decodificación usa una <see cref="Configuration"/> con solo los módulos PNG y JPEG.</item>
/// </list>
/// </summary>
internal static class PngJpegImageDecoding
{
    /// <summary>
    /// HU #13371 (hallazgo L2 de seguridad) — tope de píxeles para toda imagen de usuario que se decodifica
    /// con ImageSharp (firma del FUR, firma de la impronta). 25 M px (p. ej. 6000x4166) son ~100 MB en
    /// <see cref="Rgba32"/>: admite la foto de un móvil (12 M px) y deja fuera una bomba de descompresión
    /// (un PNG/JPEG de pocos KB que declara 30000x30000 en la cabecera pediría 3,6 GB). Las rúbricas que
    /// extrae <see cref="PdfXObjectPngDecoder"/> siguen con su tope propio, más estricto (4 M px).
    /// </summary>
    internal const int MaxDecodePixels = 25_000_000;

    /// <summary>
    /// Configuración de ImageSharp con solo los códecs PNG y JPEG registrados y sus decodificadores con
    /// <c>IgnoreMetadata = true</c> (HU #13371, L2): no se leen ni se conservan EXIF, XMP, IPTC, ICC (APP1,
    /// APP2, APP13 del JPEG) ni los chunks de texto/EXIF del PNG. El decodificador PNG de 2.1.11 no
    /// interpreta iCCP en ningún caso (lo salta).
    /// </summary>
    internal static Configuration PngJpegOnly { get; } = CreatePngJpegOnly();

    private static Configuration CreatePngJpegOnly()
    {
        var configuration = new Configuration(new PngConfigurationModule(), new JpegConfigurationModule());
        configuration.ImageFormatsManager.SetDecoder(PngFormat.Instance, new PngDecoder { IgnoreMetadata = true });
        configuration.ImageFormatsManager.SetDecoder(JpegFormat.Instance, new JpegDecoder { IgnoreMetadata = true });
        return configuration;
    }

    /// <summary>True si los bytes empiezan por la firma de archivo PNG o JPEG.</summary>
    internal static bool IsAllowed(byte[]? bytes) => IdentitySignatureImageFormat.IsSupported(bytes);

    /// <summary>
    /// Lanza la misma excepción que ImageSharp ante un formato desconocido si los bytes no son PNG/JPEG.
    /// Se llama antes de <c>XImage.FromStream</c> para que cada sitio caiga en su rama de imagen inválida.
    /// </summary>
    internal static void EnsureAllowed(byte[]? bytes)
    {
        if (!IsAllowed(bytes))
            throw new UnknownImageFormatException("Solo se admiten imágenes PNG o JPEG.");
    }

    /// <summary>Lee la cabecera (dimensiones) sin decodificar píxeles. Null si no es PNG/JPEG.</summary>
    internal static IImageInfo? Identify(byte[]? bytes) =>
        IsAllowed(bytes) ? Image.Identify(PngJpegOnly, bytes!, out _) : null;

    /// <summary>
    /// Decodifica PNG/JPEG dentro de <see cref="MaxDecodePixels"/>. Otro formato lanza
    /// <see cref="UnknownImageFormatException"/>; unas dimensiones de cabecera fuera del tope lanzan
    /// <see cref="InvalidImageContentException"/> sin reservar píxeles (HU #13371, L2).
    /// </summary>
    internal static Image<Rgba32> Load(byte[]? bytes)
    {
        EnsureAllowed(bytes);
        return LoadBounded(bytes, MaxDecodePixels)
            ?? throw new InvalidImageContentException("La imagen supera el tope de píxeles admitido.");
    }

    /// <summary>
    /// Bug #13304 — decodificación acotada: solo PNG/JPEG y dimensiones de cabecera dentro de
    /// <paramref name="maxPixels"/> ANTES de reservar píxeles. Null si no cumple.
    /// </summary>
    internal static Image<Rgba32>? LoadBounded(byte[]? bytes, int maxPixels)
    {
        var info = Identify(bytes);
        if (info is null || info.Width <= 0 || info.Height <= 0
            || (long)info.Width * info.Height > maxPixels)
            return null;

        return Image.Load<Rgba32>(PngJpegOnly, bytes!);
    }

    /// <summary>
    /// HU #13371 (hallazgo L2 de seguridad) — lo único que se entrega a PdfSharpCore (<c>XImage.FromStream</c>,
    /// que decodifica con la configuración por defecto de ImageSharp): la imagen se decodifica acotada con
    /// <see cref="LoadBounded"/> y se vuelve a codificar como PNG limpio (solo IHDR/IDAT/IEND: sin ICC, EXIF,
    /// texto, gAMA ni pHYs). Conserva dimensiones y alfa (RGBA si algún píxel no es opaco, RGB si no).
    /// Null si no es PNG/JPEG, supera <paramref name="maxPixels"/> o no se puede decodificar: cada sitio lo
    /// trata como imagen inválida.
    /// </summary>
    internal static byte[]? ReencodeToCleanPng(byte[]? bytes, int maxPixels = MaxDecodePixels)
    {
        try
        {
            using var image = LoadBounded(bytes, maxPixels);
            if (image is null)
                return null;

            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;

            var encoder = new PngEncoder
            {
                ColorType = HasTranslucentPixel(image) ? PngColorType.RgbWithAlpha : PngColorType.Rgb,
                BitDepth = PngBitDepth.Bit8,
                ChunkFilter = PngChunkFilter.ExcludeAll,
                IgnoreMetadata = true,
            };
            using var ms = new MemoryStream();
            image.Save(ms, encoder);
            return ms.ToArray();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or InsufficientExecutionStackException))
        {
            return null;
        }
    }

    /// <summary>
    /// <see cref="ReencodeToCleanPng"/> con el tope por defecto; lanza <see cref="UnknownImageFormatException"/>
    /// si la imagen no es válida, para que cada sitio caiga en su rama de imagen inválida.
    /// </summary>
    internal static byte[] RequireCleanPng(byte[]? bytes) =>
        ReencodeToCleanPng(bytes, MaxDecodePixels)
        ?? throw new UnknownImageFormatException("Solo se admiten imágenes PNG o JPEG dentro del tope de píxeles.");

    private static bool HasTranslucentPixel(Image<Rgba32> image)
    {
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (image[x, y].A < 255)
                    return true;
            }
        }

        return false;
    }
}
