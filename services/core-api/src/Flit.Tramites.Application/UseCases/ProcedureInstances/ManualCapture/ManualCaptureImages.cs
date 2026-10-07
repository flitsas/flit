namespace Flit.Tramites.Application.UseCases.ProcedureInstances.ManualCapture;

/// <summary>Formato de imagen reconocido por CONTENIDO (magic bytes), no por Content-Type ni por extensión.</summary>
public enum ManualCaptureImageFormat
{
    Unknown,
    Jpeg,
    Png,
    Webp,
}

/// <summary>
/// HU #13290 (Feature #13281 B) — límites y validación de las imágenes de la captura manual.
/// <para>
/// Tamaño máximo: <see cref="MaxImageBytes"/> (5 MiB) para rostro, anverso y reverso — una foto de cámara en vivo a 1080p o
/// 4K en JPEG/WebP pesa 0,2–3 MiB — y <see cref="MaxSignatureBytes"/> (2 MiB) para la firma, un PNG de lienzo que rara vez pasa de
/// 200 KiB. El total de la petición se acota con <see cref="MaxRequestBytes"/>.
/// </para>
/// </summary>
public static class ManualCaptureImages
{
    public const long MaxImageBytes = 5L * 1024 * 1024;
    public const long MaxSignatureBytes = 2L * 1024 * 1024;

    /// <summary>Suma de los 4 máximos más holgura para los encabezados multipart.</summary>
    public const long MaxRequestBytes = 3 * MaxImageBytes + MaxSignatureBytes + 1024 * 1024;

    /// <summary>Reconoce JPEG (FF D8 FF), PNG (89 50 4E 47 0D 0A 1A 0A) y WebP (RIFF....WEBP) por sus primeros bytes.</summary>
    public static ManualCaptureImageFormat Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ManualCaptureImageFormat.Jpeg;

        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length >= png.Length && bytes[..png.Length].SequenceEqual(png))
            return ManualCaptureImageFormat.Png;

        if (bytes.Length >= 12
            && bytes[..4].SequenceEqual("RIFF"u8)
            && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
            return ManualCaptureImageFormat.Webp;

        return ManualCaptureImageFormat.Unknown;
    }

    /// <summary>Rostro, anverso y reverso: JPEG, PNG o WebP.</summary>
    public static bool IsAllowedPhoto(ManualCaptureImageFormat format) =>
        format is ManualCaptureImageFormat.Jpeg or ManualCaptureImageFormat.Png or ManualCaptureImageFormat.Webp;

    /// <summary>La firma trazada es siempre un PNG (lienzo con transparencia).</summary>
    public static bool IsAllowedSignature(ManualCaptureImageFormat format) => format == ManualCaptureImageFormat.Png;

    /// <summary>Content-Type real del formato detectado por contenido (HU #13297: se entrega al Super Admin tal cual).</summary>
    public static string ContentType(ManualCaptureImageFormat format) => format switch
    {
        ManualCaptureImageFormat.Jpeg => "image/jpeg",
        ManualCaptureImageFormat.Png => "image/png",
        ManualCaptureImageFormat.Webp => "image/webp",
        _ => "application/octet-stream",
    };

    public static string Extension(ManualCaptureImageFormat format) => format switch
    {
        ManualCaptureImageFormat.Jpeg => "jpg",
        ManualCaptureImageFormat.Png => "png",
        ManualCaptureImageFormat.Webp => "webp",
        _ => "bin",
    };
}
