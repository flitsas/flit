using Flit.Admin.Application.Plataforma.Notificaciones;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Admin.Application.Companies.Settings;
using Flit.Infrastructure.Email;
using Flit.Infrastructure.Notifications.Catalog;
using Flit.Infrastructure.Notifications.Preview;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Notifications.Admin;

/// <summary>
/// Envío de prueba de una plantilla del catálogo al buzón de pruebas, con límite de frecuencia
/// persistido (HU #11368, Feature #11349, la de mayor riesgo del Feature). Desde la HU #11371
/// (Feature #11349, cierra el retorno-temprano fijo que impedía usar el canal API Renting) el envío
/// SÍ se conecta al enrutador (<see cref="IExplicitChannelEmailSender"/>) — ver comentario de AC4
/// más abajo.
/// </summary>
/// <remarks>
/// <para>
/// <b>Orden de validación (por qué):</b> plantilla (AC6) y buzón (AC3) se comprueban PRIMERO y
/// NUNCA consumen el enfriamiento — son errores de entrada, no intentos de envío. HU #11371 añade
/// una tercera comprobación de entrada, TAMBIÉN sin I/O y SIN consumir enfriamiento: plantilla de
/// cuenta + canal <c>TENANT_API</c> (ver <see cref="NotificationTestSendOutcome.TemplateChannelMismatch"/>).
/// El enfriamiento (AC2) se evalúa ANTES de resolver canal o renderizar: así CUALQUIER plantilla
/// dentro de la ventana responde 429 sin tocar el transporte, sin importar si esa plantilla en
/// particular habría renderizado bien o no. Disponibilidad del canal (AC4) y render (AC5) se
/// resuelven DESPUÉS del enfriamiento y TAMPOCO lo consumen: solo se sella <c>last_test_sent_at</c>
/// justo ANTES de invocar el envío — mismo patrón que
/// <c>AnalyticsSchedulerProcessor.ClaimAndSealScheduleAsync</c> (sellar ANTES de enviar, dentro de
/// la misma operación de guardado, para que una segunda solicitud concurrente pierda la carrera de
/// concurrencia optimista sobre <c>row_version</c> en vez de colarse mientras el primer envío -lento-
/// todavía no termina).
/// </para>
/// <para>
/// <b>AC4 — ya NO es una desviación deliberada del literal.</b> El AC original pedía causa "canal
/// sin adaptador" para API Renting; hasta la HU #11371 este banco de pruebas respondía SIEMPRE con
/// causa de configuración incompleta para <c>TENANT_API</c>, porque el enrutamiento por canal (HU
/// #11362) todavía no existía como capacidad alcanzable. Esa HU #11362 SÍ se entregó, y la #11371
/// conecta este servicio a ella a través de <see cref="IExplicitChannelEmailSender"/> — la MISMA
/// interfaz cuya disponibilidad consulta <c>NotificationChannelsAdminService</c> (HU #11367) para
/// <c>GET .../canales</c>, así que ambos endpoints nunca divergen sobre si el canal está disponible.
/// Con <c>TENANT_API</c> seleccionado: si el adaptador Renting NO está registrado en este ambiente
/// (así se despliega hoy), la causa sigue siendo configuración incompleta y AÚN NO consume
/// enfriamiento; si SÍ está registrado, el envío se intenta de verdad por el adaptador — nunca por
/// Colas FLIT como respaldo silencioso.
/// </para>
/// <para>
/// <b>AC8 — cómo se detecta el transporte de consola.</b> El puerto <see cref="IEmailSender"/> NO
/// sirve para esto (<c>ConsoleEmailSender</c> devuelve <c>Sent</c> igual que <c>SmtpEmailSender</c>).
/// Se usa <see cref="EmailTransportDescriptor"/>, calculado UNA vez en el arranque con la MISMA
/// condición que decide qué implementación registrar — y solo aplica al canal <c>FLIT_SMTP</c>: el
/// canal <c>TENANT_API</c> nunca es transporte de consola (HU #11371), sin importar el ambiente.
/// </para>
/// <para>
/// <b>HU #11371 — se salta el decorador de bitácora.</b> Este servicio llama a
/// <see cref="IExplicitChannelEmailSender"/> (el enrutador) directamente, sin pasar por el
/// <c>IEmailSender</c> decorado que usan los 6 puntos de envío de producción — así que el intento no
/// queda en <c>admin.notification_delivery_logs</c>. Para no dejar este camino mudo, cada intento
/// (exitoso o fallido) se registra con <see cref="ILogger"/> — plantilla, canal, causa y éxito, NUNCA
/// el destinatario (dato personal, Ley 1581).
/// </para>
/// </remarks>
internal sealed partial class NotificationTestSendAdminService(
    FlitDbContext db,
    IExplicitChannelEmailSender explicitChannelSender,
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

        // HU #11371 — plantilla de cuenta + canal TENANT_API: error de entrada, SIN I/O, SIN
        // consumir enfriamiento. Las 3 plantillas de NotificationModule.Security ignoran el canal
        // por diseño (AC3 de la HU #11362) y en producción SIEMPRE salen por Colas FLIT — forzar el
        // envío por TENANT_API aquí haría creer que esos correos se enrutan, que es justo el
        // Bug #11311 que esta ola cierra.
        if (channel == NotificationChannel.TenantApi && descriptor.Module == NotificationModule.Security)
        {
            LogTemplateChannelMismatch(logger, descriptor.Id);
            return NotificationTestSendResult.Failure(
                NotificationTestSendOutcome.TemplateChannelMismatch,
                "Esta plantilla es un correo de cuenta: ignora el canal por diseño y siempre sale "
                    + "por Colas FLIT. Selecciona el canal FLIT_SMTP para probarla.",
                templateId: descriptor.Id,
                channel: SettingsWire.ToWire(channel));
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

        // AC4 — HU #11371: disponibilidad del canal consultada con la MISMA regla que usa el envío
        // (IExplicitChannelEmailSender.IsChannelAvailable) — la misma que consume
        // NotificationChannelsAdminService para GET .../canales. Se evalúa ANTES de sellar el
        // enfriamiento: un problema de configuración no puede obligar a esperar 5 minutos para
        // probar el otro canal.
        if (!explicitChannelSender.IsChannelAvailable(channel))
        {
            LogChannelNotAvailable(logger, channel);
            return NotificationTestSendResult.Failure(
                NotificationTestSendOutcome.ChannelNotConfigured,
                "El canal API Renting no está disponible para el envío de prueba en este ambiente.",
                templateId: descriptor.Id,
                channel: SettingsWire.ToWire(channel));
        }

        // HU #11371 — remitente resuelto por canal: FlitSmtp usa la configuración SMTP; TenantApi
        // usa el remitente propio del canal Renting (el que verá quien reciba el correo).
        var (senderEmail, senderName) = ResolveSender(channel);
        if (senderEmail is null)
        {
            return NotificationTestSendResult.Failure(
                NotificationTestSendOutcome.ChannelNotConfigured,
                "El canal seleccionado no tiene remitente configurado.",
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

        // HU #11371 — envía por el canal explícito elegido, SIN resolver política de tenant y SIN el
        // bypass de correos de cuenta (ya se descartó ese caso arriba). Se salta el decorador de
        // bitácora: por eso el registro propio de abajo.
        var sendResult = await explicitChannelSender.SendAsync(channel, message, ct).ConfigureAwait(false);

        // AC8 — solo el canal FlitSmtp puede ser transporte de consola; TenantApi nunca lo es.
        var isConsoleTransport = channel == NotificationChannel.FlitSmtp && transportDescriptor.IsConsole;

        var outcome = sendResult.Success
            ? NotificationTestSendOutcome.Sent
            : NotificationTestSendOutcome.TransportFailed;
        LogSendAttempt(logger, descriptor.Id, channel, outcome, sendResult.Success);

        return new NotificationTestSendResult(
            Success: sendResult.Success,
            Outcome: outcome,
            Message: BuildMessage(sendResult, isConsoleTransport),
            TemplateId: descriptor.Id,
            Channel: SettingsWire.ToWire(channel),
            SenderEmail: senderEmail,
            SenderName: senderName,
            SentAt: now,
            RetryAfterSeconds: null,
            IsConsoleTransport: isConsoleTransport,
            RecipientDiverted: sendResult.RecipientDiverted);
    }

    /// <summary>
    /// HU #11371 — remitente por canal: FlitSmtp lee <see cref="EmailSettings.DefaultSenderEmail"/> /
    /// <see cref="EmailSettings.DefaultSenderName"/>; TenantApi lee
    /// <see cref="RentingChannelOptions.SendEmailSenderEmail"/> /
    /// <see cref="RentingChannelOptions.SendEmailSenderUsername"/> — el mismo remitente que usa el
    /// camino normal del router para este canal (ver <c>TenantChannelEmailRouter.SendViaChannelAsync</c>).
    /// </summary>
    private (string? Email, string? Name) ResolveSender(NotificationChannel channel) => channel switch
    {
        NotificationChannel.TenantApi => (
            NullIfBlank(rentingOptions.Value.SendEmailSenderEmail),
            NullIfBlank(rentingOptions.Value.SendEmailSenderUsername)),
        _ => (NullIfBlank(emailSettings.DefaultSenderEmail), NullIfBlank(emailSettings.DefaultSenderName)),
    };

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
        Message = "Banco de pruebas de notificaciones: la plantilla '{TemplateId}' es un correo de "
            + "cuenta e ignora el canal por diseño (AC3). No se envía por TENANT_API.")]
    private static partial void LogTemplateChannelMismatch(ILogger logger, string templateId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Banco de pruebas de notificaciones: el canal {Channel} no está disponible en este "
            + "ambiente.")]
    private static partial void LogChannelNotAvailable(ILogger logger, NotificationChannel channel);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Banco de pruebas de notificaciones: falló el render de la muestra de la "
            + "plantilla '{TemplateId}'.")]
    private static partial void LogRenderFailed(ILogger logger, Exception exception, string templateId);

    // HU #11371 — se salta el decorador de bitácora (llama al enrutador directamente): este es el
    // único registro que queda del intento. NUNCA incluye el destinatario (dato personal, Ley 1581).
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Banco de pruebas de notificaciones: intento de envío (plantilla={TemplateId}, "
            + "canal={Channel}, resultado={Outcome}, éxito={Success}).")]
    private static partial void LogSendAttempt(
        ILogger logger, string templateId, NotificationChannel channel, NotificationTestSendOutcome outcome, bool success);
}
