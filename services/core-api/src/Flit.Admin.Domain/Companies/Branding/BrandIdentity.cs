namespace Flit.Admin.Domain.Companies.Branding;

/// <summary>
/// Identidad de marca resuelta y lista para exponer (pública, sesión, tema de correo). Nace en
/// HU #12412 porque el snapshot publicado de <see cref="TenantBranding"/> es su única fuente; la
/// consumen #12418 (resolución pública/sesión) y #12428 (tema de correo). <see cref="Flit"/> es la
/// identidad de respaldo: cualquier caso negativo (dominio desconocido, marca sin publicar, fallo de
/// BD/storage, cabeza no MARCA_BLANCA) resuelve a esta misma constante, byte a byte.
/// </summary>
public sealed record BrandIdentity(string PlatformName, Guid? LogoId, BrandColors Colors, int Version)
{
    public static readonly BrandIdentity Flit = new(
        PlatformName: "FLIT 2.0",
        LogoId: null,
        Colors: new BrandColors("#162744", "#557EFF", "#FFFFFF"),
        Version: 0);
}
