using Flit.Admin.Application.Plataforma.Notificaciones;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Admin.Application.Companies.Settings;
using Flit.Infrastructure.Email;
using Flit.Infrastructure.Notifications.Catalog;
using Flit.Infrastructure.Notifications.Preview;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Notifications.Admin;

/// <summary>
/// Envío de prueba de una plantilla del catálogo al buzón de pruebas, con límite de frecuencia
/// persistido (HU #11368, Feature #11349, la de mayor riesgo del Feature).
/// </summary>
/// <remarks>
/// <para>
/// <b>Orden de validación (por qué):</b> plantilla (AC6) y buzón (AC3) se comprueban PRIMERO y
/// NUNCA consumen el enfriamiento — son errores de entrada, no intentos de envío. El enfriamiento
/// (AC2) se evalúa ANTES de resolver canal o renderizar: así CUALQUIER plantilla dentro de la
/// ventana responde 429 sin tocar el transporte, sin importar si esa plantilla en particular
/// habría renderizado bien o no. Canal (AC4) y render (AC5) se resuelven DESPUÉS del enfriamiento
/// y TAMPOCO lo consumen: solo se sella <c>last_test_sent_at</c> justo ANTES de invocar
/// <see cref="IEmailSender.SendAsync"/> — mismo patrón que
/// <c>AnalyticsSchedulerProcessor.ClaimAndSealScheduleAsync</c> (sellar ANTES de enviar, dentro de
/// la misma operación de guardado, para que una segunda solicitud concurrente pierda la carrera de
/// concurrencia optimista sobre <c>row_version</c> en vez de colarse mientras el primer envío -lento-
/// todavía no termina).
/// </para>
/// <para>
/// <b>AC4 — desviación deliberada del literal:</b> el AC original pide causa "canal sin adaptador"
/// para API Renting. Ese texto asumía que el Feature #11348 no se entregaría en esta ola; el
/// adaptador SÍ entra en el mismo PR (HU #11361), así que esa causa sería falsa el día del
/// despliegue. En su lugar: con <c>TENANT_API</c> seleccionado, este banco de pruebas responde
/// SIEMPRE con causa de configuración incompleta — no solo cuando <see cref="RentingChannelOptions.Enabled"/>
/// es <c>false</c> (que es como se despliega hoy), sino también si algún día se habilita, porque el
/// enrutamiento por canal (HU #11362) todavía no existe: no hay forma de que ESTE servicio invoque
/// el adaptador Renting aunque exista y esté habilitado. En ningún caso sale correo por Colas FLIT
/// cuando el canal elegido es el otro.
/// </para>
/// <para>
/// <b>AC8 — cómo se detecta el transporte de consola:</b> el puerto <see cref="IEmailSender"/> NO
/// sirve para esto (<c>ConsoleEmailSender</c> devuelve <c>Sent</c> igual que <c>SmtpEmailSender</c>,
/// y el <c>IEmailSender</c> inyectado es el decorador de bitácora, no el transporte concreto). Se
/// usa <see cref="EmailTransportDescriptor"/>, calculado UNA vez en el arranque con la MISMA
/// condición que decide qué implementación registrar.
/// </para>
/// </remarks>
internal sealed partial class NotificationTestSendAdminService(
    FlitDbContext db,
    IEmailSender emailSender,
    EmailSettings emailSettings,
    IOptions<RentingChannelOptions> rentingOptions,
    EmailTransportDescriptor transportDescriptor,
    TimeProvider timeProvider,
    ILogger<NotificationTestSendAdminService> logger) : INotificationTestSendAdminService
{
    // Ventana de enfriamiento entre envíos de prueba (AC2). Constante de producto: el banco de
    // pruebas es de uso ocasional del SuperAdmin, no un canal operativo con SLA distinto por
    // ambiente — no se modela como configuración.
    private static readonly TimeSpan CooldownWindow = TimeSpan.FromMinutes(5);

    public async Task<NotificationTestSendResult> SendAsync(
        SendNotificationTestRequest request, Guid? userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // AC6 — plantilla inexistente: 404, SIN tocar el enfriamiento.
        if (!NotificationTemplateCatalog.TryResolve(request.TemplateId ?? string.Empty, out var descriptor))
        {
            return NotificationTestSendResult.Failure(
                NotificationTestSendOutcome.TemplateNotFound,
                "La plantilla solicitada no existe en el catálogo.",
                templateId: request.TemplateId);
        }

        // Canal fuera del catálogo: mismo trato que la plantilla inexistente — error de entrada,
        // no consume enfriamiento.
        if (!SettingsWire.TryParseChannel(request.Channel, out var channel))
        {
            return NotificationTestSendResult.Failure(
                NotificationTestSendOutcome.InvalidChannel,
                $"Canal inválido. Valores permitidos: {SettingsWire.AllowedChannels}.",
                templateId: descriptor.Id,
                channel: request.Channel);
        }

        var row = await GetRowAsync(ct).ConfigureAwait(false);

        // AC3 — sin buzón configurado: 400, SIN tocar el enfriamiento.
        if (string.IsNullOrWhiteSpace(row.TestRecipientEmail))
        {
            return NotificationTestSendResult.Failure(
                NotificationTestSendOutcome.MailboxNotConfigured,
                "El buzón de pruebas no está configurado.",
                templateId: descriptor.Id,
                channel: SettingsWire.ToWire(channel));
        }

        var now = timeProvider.GetUtcNow();

        // AC2 — límite de frecuencia: se evalúa ANTES de resolver canal o renderizar (ver comentario
        // de clase). Cualquier plantilla dentro de la ventana responde 429 sin tocar el transporte.
        if (row.LastTestSentAt is { } lastSentAt)
        {
            var elapsed = now - lastSentAt;
            if (elapsed < CooldownWindow)
            {
                var retryAfter = (int)Math.Ceiling((CooldownWindow - elapsed).TotalSeconds);
                return NotificationTestSendResult.Failure(
                    NotificationTestSendOutcome.RateLimited,
                    $"Se alcanzó el límite de envíos de prueba. Intenta de nuevo en {retryAfter} segundos.",
                    templateId: descriptor.Id,
                    channel: SettingsWire.ToWire(channel),
                    retryAfterSeconds: Math.Max(retryAfter, 1));
            }
        }

        // AC4 — ver comentario de clase: TENANT_API SIEMPRE cae en configuración incompleta en este
        // banco de pruebas (el enrutamiento por canal, HU #11362, todavía no existe).
        if (channel == NotificationChannel.TenantApi)
        {
            LogTenantApiNotAvailable(logger, rentingOptions.Value.Enabled);
            return NotificationTestSendResult.Failure(
                NotificationTestSendOutcome.ChannelNotConfigured,
                "El canal API Renting no está disponible para el envío de prueba en este ambiente.",
                templateId: descriptor.Id,
                channel: SettingsWire.ToWire(channel));
        }

        var senderEmail = NullIfBlank(emailSettings.DefaultSenderEmail);
        var senderName = NullIfBlank(emailSettings.DefaultSenderName);
        if (senderEmail is null)
        {
            return NotificationTestSendResult.Failure(
                NotificationTestSendOutcome.ChannelNotConfigured,
                "El canal Colas FLIT no tiene remitente configurado.",
                templateId: descriptor.Id,
                channel: SettingsWire.ToWire(channel));
        }

        // AC5 — render de muestra. Un fallo aquí es defensivo (el catálogo resolvió un id sin
        // muestra registrada en RenderSample): se reporta como fallo de render, SIN enviar ni sellar.
        string subject, html;
        try
        {
            (subject, html) = RenderSample(descriptor.Id);
        }
        catch (Exception ex)
        {
            LogRenderFailed(logger, ex, descriptor.Id);
            return NotificationTestSendResult.Failure(
                NotificationTestSendOutcome.RenderFailed,
                "No fue posible renderizar la muestra de la plantilla solicitada.",
                templateId: descriptor.Id,
                channel: SettingsWire.ToWire(channel));
        }

        // Sella ANTES de enviar (ver comentario de clase). Si otra solicitud ya selló la fila entre
        // la lectura de arriba y este SaveChangesAsync, row_version no coincide y EF lanza
        // DbUpdateConcurrencyException — se trata igual que un límite de frecuencia vencido a la
        // carrera: ninguna de las dos ganadoras duplica el envío.
        row.LastTestSentAt = now;
        row.UpdatedAt = now;
        row.UpdatedBy = userId;
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return NotificationTestSendResult.Failure(
                NotificationTestSendOutcome.RateLimited,
                "Otra solicitud de envío de prueba se adelantó. Intenta de nuevo en unos segundos.",
                templateId: descriptor.Id,
                channel: SettingsWire.ToWire(channel),
                retryAfterSeconds: (int)CooldownWindow.TotalSeconds);
        }

        // Dato personal (Ley 1581): el buzón NUNCA se loguea ni se incluye en el mensaje devuelto.
        var message = new EmailMessage(
            TenantId: null,
            TemplateKey: descriptor.Id,
            ToEmail: row.TestRecipientEmail!,
            ToName: "Banco de pruebas de notificaciones",
            Subject: subject,
            HtmlBody: html);

        var sendResult = await emailSender.SendAsync(message, ct).ConfigureAwait(false);

        return new NotificationTestSendResult(
            Success: sendResult.Success,
            Outcome: sendResult.Success
                ? NotificationTestSendOutcome.Sent
                : NotificationTestSendOutcome.TransportFailed,
            Message: BuildMessage(sendResult, transportDescriptor.IsConsole),
            TemplateId: descriptor.Id,
            Channel: SettingsWire.ToWire(channel),
            SenderEmail: senderEmail,
            SenderName: senderName,
            SentAt: now,
            RetryAfterSeconds: null,
            IsConsoleTransport: transportDescriptor.IsConsole);
    }

    private async Task<NotificationTestSettingsRow> GetRowAsync(CancellationToken ct)
    {
        // La migración 67-HU11365 siembra exactamente una fila (índice único sobre expresión
        // constante). Este fallback solo protege un entorno de prueba donde esa migración no
        // corrió — mismo criterio que NotificationTestMailboxAdminService.GetOrSeedRowAsync.
        var row = await db.NotificationTestSettings.SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (row is not null) return row;

        row = new NotificationTestSettingsRow
        {
            Id = Guid.NewGuid(),
            TestRecipientEmail = null,
            LastTestSentAt = null,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.NotificationTestSettings.Add(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return row;
    }

    // Duplicado deliberado del despacho de AdminPlataformaNotificacionesPlantillasEndpoints
    // (Flit.Api, HU #11356): esta clase vive en Flit.Infrastructure, una capa por debajo de
    // Flit.Api, así que no puede referenciar ese endpoint. Ambos despachos consumen las MISMAS
    // muestras (SecurityEmailPreviewSample / AnalyticsEmailPreviewSample), que a su vez componen
    // con los MISMOS composers de producción — la única superficie que podría divergir es esta
    // lista de 5 ids, tan estable como el propio catálogo (AC3 de la HU #11353).
    private static (string Subject, string Html) RenderSample(string templateId) => templateId switch
    {
        "security.invitation" => ToTuple(SecurityEmailPreviewSample.BuildInvitation()),
        "security.forgot-password" => ToTuple(SecurityEmailPreviewSample.BuildForgotPassword()),
        "security.admin-reset-password" => ToTuple(SecurityEmailPreviewSample.BuildAdminResetPassword()),
        "analytics.scheduled-report" => AnalyticsEmailPreviewSample.BuildScheduledReport(),
        "analytics.alert" => AnalyticsEmailPreviewSample.BuildAlert(),
        _ => throw new InvalidOperationException(
            $"El catálogo resolvió el id '{templateId}' pero no hay muestra registrada para él."),
    };

    private static (string Subject, string Html) ToTuple(Flit.Modules.Security.Application.Auth.ComposedEmail email) =>
        (email.Subject, email.HtmlBody);

    private static string BuildMessage(EmailSendResult sendResult, bool isConsoleTransport)
    {
        if (!sendResult.Success)
            return sendResult.Message;

        // AC8 — la respuesta declara EXPLÍCITAMENTE que el transporte fue de consola y que no hubo
        // correo real; no basta con Outcome=Sent (ConsoleEmailSender también lo devuelve).
        return isConsoleTransport
            ? "Correo de prueba generado con el transporte de CONSOLA (ambiente de desarrollo): "
                + "no se envió un correo real, el mensaje quedó únicamente en el log del servidor."
            : "Correo de prueba enviado.";
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Banco de pruebas de notificaciones: canal API Renting solicitado pero no "
            + "disponible en este servicio (RentingChannelOptions.Enabled={RentingEnabled}). AC4 — "
            + "el enrutamiento por canal (HU #11362) todavía no existe.")]
    private static partial void LogTenantApiNotAvailable(ILogger logger, bool rentingEnabled);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Banco de pruebas de notificaciones: falló el render de la muestra de la "
            + "plantilla '{TemplateId}'.")]
    private static partial void LogRenderFailed(ILogger logger, Exception exception, string templateId);
}
