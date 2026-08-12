namespace Flit.Infrastructure.Notifications;

/// <summary>
/// Base URL pública de assets de correo (banner/logo). Deben ser URLs absolutas alcanzables
/// por clientes de correo; los archivos viven en <c>frontend/public/email-assets/</c>.
/// </summary>
public sealed class NotificationEmailAssetsOptions
{
    public const string SectionName = "Notifications:EmailAssets";

    /// <summary>Ej.: <c>https://dev.flitsas.online/email-assets</c> o <c>http://localhost:3000/email-assets</c>.</summary>
    public string BaseUrl { get; set; } = "https://dev.flitsas.online/email-assets";
}
