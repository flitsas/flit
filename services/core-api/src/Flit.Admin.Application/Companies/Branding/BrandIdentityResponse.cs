using Flit.Admin.Domain.Companies.Branding;

namespace Flit.Admin.Application.Companies.Branding;

/// <summary>Paleta de la respuesta pública/sesión (HU #12418, contratos-api.md §1/§2).</summary>
public sealed record BrandIdentityColorsResponse(string Primary, string Secondary, string OnPrimary)
{
    public static BrandIdentityColorsResponse From(BrandColors colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        return new BrandIdentityColorsResponse(colors.Primary, colors.Secondary, colors.OnPrimary);
    }
}

/// <summary>
/// Forma pública de la identidad de marca (HU #12418 AC1/AC3/AC5, ADR-0060 D2) — <c>GET
/// /public/branding</c> y <c>GET /me/branding</c>. PROHIBIDO agregar cualquier id de tenant, NIT,
/// correo, razón social, lista de hijos o flag <c>isDefault</c>/<c>isNetwork</c> (AC3): un dominio
/// desconocido y uno configurado deben ser indistinguibles desde este contrato.
/// </summary>
public sealed record BrandIdentityResponse(
    string PlatformName,
    string? LogoUrl,
    BrandIdentityColorsResponse Colors,
    int Version)
{
    public static BrandIdentityResponse From(BrandIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new BrandIdentityResponse(
            identity.PlatformName,
            identity.LogoId is { } logoId ? $"/api/v1/public/branding/logos/{logoId}" : null,
            BrandIdentityColorsResponse.From(identity.Colors),
            identity.Version);
    }
}
