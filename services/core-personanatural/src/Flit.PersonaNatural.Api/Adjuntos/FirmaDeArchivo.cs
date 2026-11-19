namespace Flit.PersonaNatural.Api.Adjuntos;

/// <summary>
/// Qué es de verdad un archivo (HU #14048): se mira la firma de los primeros bytes y no la extensión ni el
/// <c>Content-Type</c> que declara quien sube, que son datos del cliente. Solo se aceptan PDF, JPG, PNG y WEBP.
/// </summary>
public static class FirmaDeArchivo
{
    public const string Pdf = "application/pdf";
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Webp = "image/webp";

    private static readonly byte[] FirmaPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>El tipo real según la firma, o <c>null</c> si no es ninguno de los permitidos (un ejecutable, por ejemplo).</summary>
    public static string? Detectar(ReadOnlySpan<byte> inicio)
    {
        if (inicio.Length >= 5 && inicio[..5].SequenceEqual("%PDF-"u8))
            return Pdf;
        if (inicio.Length >= 3 && inicio[0] == 0xFF && inicio[1] == 0xD8 && inicio[2] == 0xFF)
            return Jpeg;
        if (inicio.Length >= 8 && inicio[..8].SequenceEqual(FirmaPng))
            return Png;
        if (inicio.Length >= 12 && inicio[..4].SequenceEqual("RIFF"u8) && inicio.Slice(8, 4).SequenceEqual("WEBP"u8))
            return Webp;
        return null;
    }

    /// <summary>El tipo que dice la extensión del nombre; <c>null</c> si no es una de las permitidas.</summary>
    public static string? SegunExtension(string? nombre) => Path.GetExtension(nombre ?? string.Empty).ToLowerInvariant() switch
    {
        ".pdf" => Pdf,
        ".jpg" or ".jpeg" => Jpeg,
        ".png" => Png,
        ".webp" => Webp,
        _ => null,
    };

    /// <summary>Extensión canónica del tipo real, para el nombre que se guarda.</summary>
    public static string Extension(string mime) => mime switch
    {
        Pdf => ".pdf",
        Jpeg => ".jpg",
        Png => ".png",
        _ => ".webp",
    };

    /// <summary>
    /// Nombre seguro para guardar: solo letras, dígitos, guion y guion bajo, con la extensión del tipo real y un tope de
    /// longitud. El nombre original del cliente nunca se usa tal cual (puede traer rutas o un dato personal).
    /// </summary>
    public static string NombreSeguro(string? original, string mime)
    {
        var baseNombre = Path.GetFileNameWithoutExtension(original ?? string.Empty);
        var limpio = new string(baseNombre.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray()).Trim('_');
        if (limpio.Length == 0)
            limpio = "archivo";
        if (limpio.Length > 80)
            limpio = limpio[..80];
        return limpio + Extension(mime);
    }
}
