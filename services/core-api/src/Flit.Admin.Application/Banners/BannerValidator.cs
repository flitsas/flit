namespace Flit.Admin.Application.Banners;

public static class BannerValidator
{
    public const int NameMaxLength = 200;
    public const int LinkUrlMaxLength = 2048;
    public const long MaxImageSizeBytes = 2097152;

    private const string MimePng = "image/png";
    private const string MimeJpeg = "image/jpeg";
    private const string MimeWebp = "image/webp";

    public static readonly IReadOnlyCollection<string> AllowedImageMimeTypes =
        new List<string> { MimePng, MimeJpeg, MimeWebp };

    public static string? ValidatePayload(string name, string? linkUrl, DateTimeOffset? validFrom, DateTimeOffset? validUntil)
    {
        if (string.IsNullOrWhiteSpace(name)) return "El nombre del banner es obligatorio.";
        if (name.Length > NameMaxLength) return "El nombre del banner supera la longitud maxima.";

        if (linkUrl is not null)
        {
            if (linkUrl.Length > LinkUrlMaxLength) return "El enlace supera la longitud maxima.";
            var isValidUri = Uri.TryCreate(linkUrl, UriKind.Absolute, out var uri);
            var isHttpScheme = isValidUri && (uri!.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
            if (!isHttpScheme) return "El enlace debe ser una URL http/https valida.";
        }

        if (validFrom is null != (validUntil is null)) return "Debe indicar ambas fechas de vigencia, o ninguna.";
        if (validFrom is not null && validUntil is not null && validUntil <= validFrom)
            return "La fecha de fin de vigencia debe ser posterior a la de inicio.";

        return null;
    }

    public static string? ValidateImage(string? contentType, long sizeBytes)
    {
        var isAllowedMime = contentType is not null
            && AllowedImageMimeTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
        if (!isAllowedMime) return "Formato de imagen no permitido. Use PNG, JPEG o WEBP.";
        if (sizeBytes <= 0) return "La imagen esta vacia.";
        if (sizeBytes > MaxImageSizeBytes) return "La imagen supera el tamano maximo permitido de 2 MB.";

        return null;
    }
}
