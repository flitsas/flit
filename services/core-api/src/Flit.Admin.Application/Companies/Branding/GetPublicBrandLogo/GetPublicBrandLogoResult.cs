namespace Flit.Admin.Application.Companies.Branding.GetPublicBrandLogo;

/// <summary>Resultado de <c>GetPublicBrandLogoHandler</c> (HU #12418 AC4). Mismo patrón que
/// <c>GetBannerImageResult</c> (HU #12240).</summary>
public sealed class GetPublicBrandLogoResult
{
    private GetPublicBrandLogoResult()
    {
    }

    /// <summary><c>false</c>: id inexistente, borrado o de una cabeza que dejó de ser MARCA_BLANCA —
    /// el endpoint responde 404 SIN CUERPO, indistinguible entre los tres casos (AC4).</summary>
    public bool Found { get; private init; }

    public bool IsNotModified { get; private init; }

    public string? Sha256 { get; private init; }

    public string? ContentType { get; private init; }

    public Stream? Content { get; private init; }

    public static GetPublicBrandLogoResult NotFound() => new() { Found = false };

    public static GetPublicBrandLogoResult NotModified(string sha256) =>
        new() { Found = true, IsNotModified = true, Sha256 = sha256 };

    public static GetPublicBrandLogoResult Success(string sha256, string contentType, Stream content) =>
        new() { Found = true, Sha256 = sha256, ContentType = contentType, Content = content };
}
