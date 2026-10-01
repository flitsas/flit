namespace Flit.Infrastructure.Notifications.Theme;

/// <summary>
/// Lee <c>PublicBaseUrl</c> de la MISMA sección <c>PublicBranding</c> que
/// <c>Flit.Api.RateLimiting.PublicBrandingOptions</c> (HU #12418), sin que Infrastructure dependa de
/// Flit.Api (dirección de dependencia prohibida: Api → Infrastructure, nunca al revés). Ambas clases
/// se enlazan de forma independiente contra el mismo <c>IConfiguration</c>; un cambio de valor
/// aplica a las dos.
/// </summary>
public sealed class EmailThemePublicBrandingOptions
{
    public const string SectionName = "PublicBranding";

    /// <summary>HU #12428 AC3 — base HTTPS pública usada para la URL absoluta del logotipo del tema
    /// de correo (<c>{PublicBaseUrl}/api/v1/public/branding/logos/{logoId}</c>).</summary>
    public string PublicBaseUrl { get; set; } = "https://dev.flitsas.online";
}
