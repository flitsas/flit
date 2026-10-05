using System.Globalization;
using System.Net;
using System.Text;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Infrastructure.Notifications.Identity;

/// <summary>
/// Composer del correo con el enlace de captura manual de identidad (HU #13287, Épica #13202), id estable
/// <see cref="TemplateId"/>. Mismo chrome de marca que el resto de correos (<see cref="BrandedEmailChrome"/>, tema por la red del
/// tenant: FLIT o marca blanca). Texto corto y SIN datos sensibles: ni número de documento ni el token fuera del enlace.
/// </summary>
public static class ManualCaptureEmailComposer
{
    public const string TemplateId = "identidad.captura-manual";
    public const string FlitSupportEmail = "soporte@flitsas.com";

    /// <summary>Ruta del front de la Feature B (<c>/captura-manual/[token]</c>).</summary>
    public const string CapturePath = "/captura-manual/";

    // Hora de Colombia (UTC-5 todo el año): offset fijo, sin depender de la base de zonas horarias del sistema.
    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    public static string BuildLink(string publicBaseUrl, string token)
    {
        var baseUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? "http://localhost:3000" : publicBaseUrl.Trim().TrimEnd('/');
        return baseUrl + CapturePath + Uri.EscapeDataString(token);
    }

    public static (string Subject, string Html) Compose(
        string? recipientName, string link, DateTimeOffset expiresAt, EmailTheme theme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(link);
        ArgumentNullException.ThrowIfNull(theme);

        var platform = new string((string.IsNullOrWhiteSpace(theme.PlatformName) ? EmailTheme.Flit.PlatformName : theme.PlatformName)
            .Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        var subject = $"[{platform}] Verifica tu identidad con el enlace de captura";
        var saludo = string.IsNullOrWhiteSpace(recipientName) ? "Hola," : $"Hola {Enc(recipientName.Trim())},";
        var vence = expiresAt.ToOffset(ColombiaOffset).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var color = BrandedEmailChrome.LinkColor(theme);
        var href = Enc(link);

        var sb = new StringBuilder(1024);
        sb.Append("<p style=\"margin:0 0 12px;\"><strong>").Append(saludo).Append("</strong></p>");
        sb.Append("<p style=\"margin:0 0 12px;\">Necesitamos validar tu identidad. Con el enlace podrás tomar una foto de tu rostro, ")
            .Append("de tu documento (anverso y reverso) y trazar tu firma.</p>");
        sb.Append("<p style=\"margin:0 0 16px;text-align:center;\"><a href=\"").Append(href)
            .Append("\" style=\"display:inline-block;padding:12px 24px;background-color:").Append(color)
            .Append(";color:#ffffff;text-decoration:none;font-weight:700;border-radius:6px;\">Verificar mi identidad</a></p>");
        sb.Append("<p style=\"margin:0 0 12px;\">Si el botón no abre, copia este enlace en tu navegador:<br/>")
            .Append("<a href=\"").Append(href).Append("\" style=\"color:").Append(color).Append(";word-break:break-all;\">")
            .Append(href).Append("</a></p>");
        sb.Append("<p style=\"margin:0 0 12px;\">El enlace vence en 24 horas (hasta el ").Append(vence)
            .Append(" hora de Colombia). Es personal: no lo compartas.</p>");
        sb.Append("<p style=\"margin:0;\">Si tienes problemas, escribe a <a href=\"mailto:").Append(FlitSupportEmail)
            .Append("\" style=\"color:").Append(color).Append(";\">").Append(FlitSupportEmail).Append("</a>.</p>");

        var html = BrandedEmailChrome.Wrap(theme, "VERIFICA TU IDENTIDAD", sb.ToString());
        return (subject, html);
    }

    private static string Enc(string value) => WebUtility.HtmlEncode(value);
}
