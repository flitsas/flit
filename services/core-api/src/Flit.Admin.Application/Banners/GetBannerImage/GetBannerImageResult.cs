namespace Flit.Admin.Application.Banners.GetBannerImage;

/// <summary>Resultado del caso de uso de imagen de banner (HU #12240, AC2/AC3).</summary>
public sealed class GetBannerImageResult
{
    private GetBannerImageResult()
    {
    }

    /// <summary>false: banner inexistente o eliminado — el endpoint responde 404 (AC3).</summary>
    public bool Found { get; private init; }

    /// <summary>true: If-None-Match coincidio con el ETag actual — el endpoint responde 304
    /// sin releer el binario (AC2).</summary>
    public bool IsNotModified { get; private init; }

    public string? Sha256 { get; private init; }

    public string? ContentType { get; private init; }

    public Stream? Content { get; private init; }

    public static GetBannerImageResult NotFound() => new() { Found = false };

    public static GetBannerImageResult NotModified(string sha256) =>
        new() { Found = true, IsNotModified = true, Sha256 = sha256 };

    /// <summary>El binario existe y se debe transmitir completo (ETag nuevo o sin If-None-Match).</summary>
    public static GetBannerImageResult Success(string sha256, string contentType, Stream content) =>
        new() { Found = true, Sha256 = sha256, ContentType = contentType, Content = content };
}
