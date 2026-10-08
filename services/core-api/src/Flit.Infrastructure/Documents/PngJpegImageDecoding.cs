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
    /// <summary>Configuración de ImageSharp con solo los códecs PNG y JPEG registrados.</summary>
    internal static Configuration PngJpegOnly { get; } =
        new(new PngConfigurationModule(), new JpegConfigurationModule());

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

    /// <summary>Decodifica PNG/JPEG; cualquier otro formato lanza <see cref="UnknownImageFormatException"/>.</summary>
    internal static Image<Rgba32> Load(byte[]? bytes)
    {
        EnsureAllowed(bytes);
        return Image.Load<Rgba32>(PngJpegOnly, bytes!);
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
}
