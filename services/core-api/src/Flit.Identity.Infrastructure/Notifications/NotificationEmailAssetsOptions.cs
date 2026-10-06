namespace Flit.Infrastructure.Notifications;

/// <summary>
/// Base URL pública de assets de correo (banner/logo). Deben ser URLs absolutas alcanzables
/// por clientes de correo; los archivos viven en <c>frontend/public/email-assets/</c>.
/// </summary>
public sealed class NotificationEmailAssetsOptions
{
    public const string SectionName = "Notifications:EmailAssets";

    /// <summary>
    /// Bug #13194 — respaldo SOLO para desarrollo local; cada ambiente desplegado define
    /// <c>Notifications__EmailAssets__BaseUrl</c> (p. ej. <c>https://{host-del-ambiente}/email-assets</c>).
    /// </summary>
    public const string LocalFallbackBaseUrl = "http://localhost:3000/email-assets";

    /// <summary>Ej.: <c>https://{host-del-ambiente}/email-assets</c> o <see cref="LocalFallbackBaseUrl"/>.</summary>
    public string BaseUrl { get; set; } = LocalFallbackBaseUrl;
}
