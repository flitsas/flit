using System.Globalization;
using System.Net;
using System.Text;
using Flit.Infrastructure.Notifications.Identity;
using Flit.Infrastructure.Notifications.Theme;
using Flit.Modules.Security.Domain.Auth;
using Flit.Platform.Sdk;
using Flit.Tramites.Application.Identity.Rounds;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.IdentityRounds;

/// <summary>
/// Envía la alerta de rondas de identidad por el canal de correo de FLIT (<see cref="IEmailSender"/>: el mismo de todos los avisos,
/// sin SMTP propio), a nombre de la plataforma (<see cref="PlatformTenants.Plataforma"/>, canal SMTP de FLIT: es un aviso interno de
/// operación, no un correo de la empresa). Un correo por destinatario, para que ninguno vea a los demás. Lleva TODOS los datos de la
/// persona SIN enmascarar a propósito (operación debe saber por qué no puede validar); por eso el contenido nunca va a logs: solo el
/// id de la validación y el resultado. Nunca lanza.
/// </summary>
internal sealed partial class IdentityRoundsAlertSender(
    IEmailSender emailSender,
    EmailThemePublicBrandingOptions branding,
    ILogger<IdentityRoundsAlertSender> logger) : IIdentityRoundsAlertSender
{
    public const string TemplateKey = "identidad.alerta-rondas-agotadas";

    // Cultura invariante a propósito: el formato dd/MM/yyyy HH:mm es numérico, y "es-CO" no existe en modo de globalización invariante.
    private static readonly CultureInfo Formato = CultureInfo.InvariantCulture;

    public async Task<IdentityRoundsAlertDelivery> SendAsync(
        IdentityRoundsAlertMessage message, IReadOnlyList<string> recipients, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(recipients);

        var (subject, html) = Compose(message, branding.PublicBaseUrl, EmailTheme.Flit, branding.PublicBaseUrl);
        var enviados = 0;
        var fallidos = 0;
        foreach (var destinatario in recipients)
        {
            try
            {
                var result = await emailSender.SendAsync(
                    new EmailMessage(PlatformTenants.Plataforma, TemplateKey, destinatario, string.Empty, subject, html), ct)
                    .ConfigureAwait(false);
                if (result.Success)
                    enviados++;
                else
                    fallidos++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Solo el tipo: el mensaje podría arrastrar el correo del destinatario.
                fallidos++;
                LogSendError(logger, message.ValidationId, ex.GetType().Name);
            }
        }

        if (fallidos > 0)
            LogSendFailed(logger, message.ValidationId, fallidos);
        return new IdentityRoundsAlertDelivery(enviados, fallidos);
    }

    /// <summary>Asunto y HTML de la alerta, con el mismo diseño de los correos de identidad de FLIT. Pública para pruebas; encodea cada valor.</summary>
    internal static (string Subject, string Html) Compose(
        IdentityRoundsAlertMessage m, string publicBaseUrl, EmailTheme theme, string assetsBaseUrl)
    {
        var subject = $"[FLIT] Identidad rechazada: ronda {m.RondasUsadas} de {m.RondasMaximas}";
        var fecha = m.Fecha.ToOffset(TimeSpan.FromHours(-5)).ToString("dd/MM/yyyy HH:mm", Formato);
        var cuando = m.RondasUsadas >= m.RondasMaximas
            ? "Se alcanzó el máximo de rondas: ya no se puede reenviar la validación."
            : "La ronda cerró en rechazo; aún quedan rondas disponibles.";

        var tramite = !string.IsNullOrWhiteSpace(m.TramiteReferencia)
            ? m.TramiteReferencia!
            : m.ProcedureInstanceId?.ToString() ?? "Sin trámite (prevalidación)";

        var intro = new StringBuilder();
        intro.Append("<p style=\"margin:0 0 14px;\">").Append(Enc(cuando)).Append("</p>");
        intro.Append("<table cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;margin:0 0 4px;\">");
        Row(intro, "Nombre", m.Nombre);
        Row(intro, "Tipo de documento", m.TipoDocumento);
        Row(intro, "Número de documento", m.NumeroDocumento);
        Row(intro, "Motivo del último rechazo", m.Motivo);
        Row(intro, "Rondas usadas", $"{m.RondasUsadas} de {m.RondasMaximas}");
        Row(intro, "Trámite", tramite);
        Row(intro, "Fecha", fecha);
        intro.Append("</table>");

        var baseUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? "http://localhost:3000" : publicBaseUrl.Trim().TrimEnd('/');
        var link = m.ProcedureInstanceId is Guid instanceId
            ? $"{baseUrl}/tramites/{instanceId}"
            : $"{baseUrl}/?m=validaciones";

        var content = new IdentityVerificationEmailContent(
            Subject: subject,
            Eyebrow: "Verificación de identidad",
            Title: "Validación de identidad rechazada",
            IntroHtml: intro.ToString(),
            Steps: Array.Empty<string>(),
            ButtonLabel: "Ver la validación en FLIT",
            FallbackLinkIntro: "Si el botón no funciona, copia este enlace:",
            FooterNote: "Aviso para operación: contiene datos personales. No lo reenvíes a terceros.",
            StepsHeading: "",
            RequestedByLabel: "Aviso de operación");

        var html = IdentityVerificationEmailLayout.Render(theme, assetsBaseUrl, null, link, content);
        return (subject, html);
    }

    private static void Row(StringBuilder sb, string label, string value) =>
        sb.Append("<tr><td style=\"font-weight:bold;\">").Append(Enc(label)).Append("</td><td>").Append(Enc(value)).Append("</td></tr>");

    private static string Enc(string value) => WebUtility.HtmlEncode(value);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Rondas de identidad: {Fallidos} correo(s) de la alerta de la validación {ValidationId} no salieron.")]
    private static partial void LogSendFailed(ILogger logger, Guid validationId, int fallidos);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Rondas de identidad: error al enviar la alerta de la validación {ValidationId} ({ErrorType}).")]
    private static partial void LogSendError(ILogger logger, Guid validationId, string errorType);
}
