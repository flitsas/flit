namespace Flit.Modules.Security.Application.Auth;

/// <summary>
/// HU #13003 (FLIT Suite A-12) — URLs públicas de los correos de seguridad (invitación, recuperación, bienvenida) por
/// ambiente. Antes las plantillas no recibían la base de recursos y todos los ambientes cargaban imágenes y enlaces
/// de DEV. Vacías, las plantillas usan su valor por defecto (el que fijan las pruebas de paridad y la vista previa).
/// </summary>
public sealed class EmailLinksOptions
{
    public const string SectionName = "EmailLinks";

    /// <summary>Base de las imágenes del correo (<c>…/email-assets</c>). Por defecto, <c>Notifications:EmailAssets:BaseUrl</c>.</summary>
    public string AssetsBaseUrl { get; set; } = string.Empty;

    /// <summary>Enlace de ingreso del correo de bienvenida. Por defecto, <c>PublicBranding:PublicBaseUrl</c> + <c>/login</c>.</summary>
    public string LoginUrl { get; set; } = string.Empty;
}
