using System.Globalization;
using System.Net;
using System.Text;
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

    private static readonly CultureInfo Es = new("es-CO");

    public async Task<IdentityRoundsAlertDelivery> SendAsync(
        IdentityRoundsAlertMessage message, IReadOnlyList<string> recipients, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(recipients);

        var (subject, html) = Compose(message, branding.PublicBaseUrl);
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

    /// <summary>Asunto y HTML de la alerta. Pública para pruebas; encodea cada valor.</summary>
    internal static (string Subject, string Html) Compose(IdentityRoundsAlertMessage m, string publicBaseUrl)
    {
        var subject = $"[FLIT] Identidad rechazada: ronda {m.RondasUsadas} de {m.RondasMaximas}";
        var fecha = m.Fecha.ToOffset(TimeSpan.FromHours(-5)).ToString("dd/MM/yyyy HH:mm", Es);
        var cuando = m.RondasUsadas >= m.RondasMaximas
            ? "Se alcanzó el máximo de rondas: ya no se puede reenviar la validación."
            : "La ronda cerró en rechazo; aún quedan rondas disponibles.";

        var tramite = !string.IsNullOrWhiteSpace(m.TramiteReferencia)
            ? m.TramiteReferencia!
            : m.ProcedureInstanceId?.ToString() ?? "Sin trámite (prevalidación)";

        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#162244;\">");
        sb.Append("<h2 style=\"margin:0 0 12px;\">Validación de identidad rechazada</h2>");
        sb.Append("<p style=\"margin:0 0 16px;\">").Append(Enc(cuando)).Append("</p>");
        sb.Append("<table cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;\">");
        Row(sb, "Nombre", m.Nombre);
        Row(sb, "Tipo de documento", m.TipoDocumento);
        Row(sb, "Número de documento", m.NumeroDocumento);
        Row(sb, "Motivo del último rechazo", m.Motivo);
        Row(sb, "Rondas usadas", $"{m.RondasUsadas} de {m.RondasMaximas}");
        Row(sb, "Trámite", tramite);
        Row(sb, "Fecha", fecha);
        sb.Append("</table>");

        if (m.ProcedureInstanceId is Guid instanceId)
        {
            var baseUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? "http://localhost:3000" : publicBaseUrl.Trim().TrimEnd('/');
            var url = $"{baseUrl}/tramites/{instanceId}";
            sb.Append("<p style=\"margin:16px 0 0;\"><a href=\"").Append(Enc(url)).Append("\">Ver la ficha en FLIT</a></p>");
        }

        sb.Append("</div>");
        return (subject, sb.ToString());
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
