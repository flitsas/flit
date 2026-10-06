using System.Net;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Infrastructure.Notifications.Identity;

/// <summary>
/// Composer del correo con el enlace de captura manual de identidad (HU #13287, Épica #13202), id estable
/// <see cref="TemplateId"/>. Logo y producto del tema de correo de la plataforma (<see cref="EmailTheme"/>, por la red del
/// tenant: FLIT o marca blanca), con el diseño de verificación de identidad (cabecera, pasos numerados, botón). Texto corto y SIN
/// datos sensibles: ni número de documento ni el token fuera del enlace.
/// </summary>
public static class ManualCaptureEmailComposer
{
    public const string TemplateId = "identidad.captura-manual";
    public const int ValidityHours = 24;

    /// <summary>Ruta del front de la Feature B (<c>/captura-manual/[token]</c>).</summary>
    public const string CapturePath = "/captura-manual/";

    public static string BuildLink(string publicBaseUrl, string token)
    {
        var baseUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? "http://localhost:3000" : publicBaseUrl.Trim().TrimEnd('/');
        return baseUrl + CapturePath + Uri.EscapeDataString(token);
    }

    /// <summary>
    /// Compone el correo del enlace de captura (asunto + HTML) sobre <see cref="IdentityVerificationEmailLayout"/>: la misma
    /// función sirve al envío real (<see cref="EmailManualCaptureLinkNotifier"/>) y a la previsualización/prueba del módulo de
    /// notificaciones (<c>NotificationSampleRenderer</c>), así que hay UNA sola fuente de HTML.
    /// </summary>
    public static (string Subject, string Html) Compose(
        string? recipientName, string link, EmailTheme theme, string assetsBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(link);
        ArgumentNullException.ThrowIfNull(theme);

        var product = IdentityVerificationEmailLayout.ProductName(theme);
        var content = BuildContent(product);
        var html = IdentityVerificationEmailLayout.Render(theme, assetsBaseUrl, recipientName, link, content);
        return (content.Subject, html);
    }

    /// <summary>
    /// Texto de la variante «enlace inicial». La variante «rechazo» (enlace nuevo tras una validación rechazada) reutiliza el
    /// mismo <see cref="IdentityVerificationEmailLayout"/> con su propio <see cref="IdentityVerificationEmailContent"/>
    /// (banner, título, botón y pasos propios), sin duplicar HTML.
    /// </summary>
    internal static IdentityVerificationEmailContent BuildContent(string product) => new(
        Subject: $"[{product}] Verifica tu identidad",
        Eyebrow: "Verificación de identidad",
        Title: "Verifica tu identidad",
        IntroHtml: $"<strong style=\"color:#162244;\">{Enc(product)}</strong> necesita confirmar tu identidad para continuar y es quien guarda tus datos.",
        Steps: ["Valida tus datos", "Realiza la validación biométrica", "Captura los datos del documento", "Firma"],
        ButtonLabel: "Verificar mi identidad",
        FallbackLinkIntro: "Si el botón no funciona, copia este enlace:",
        FooterNote: $"El enlace es personal, de un solo uso y vale {ValidityHours} horas. Ábrelo en tu celular.");

    private static string Enc(string value) => WebUtility.HtmlEncode(value);
}
