using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications.Catalog;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Notifications.Routing;

/// <summary>
/// HU #11371 (Feature #11349, cierra el retorno-temprano fijo del banco de pruebas de
/// notificaciones) — capacidad de enviar por un canal EXPLÍCITO, sin resolver la política del
/// tenant y SIN la excepción de correos de cuenta (AC3 de <see cref="TenantChannelEmailRouter"/>).
/// </summary>
/// <remarks>
/// Único consumidor previsto: el banco de pruebas de notificaciones
/// (<c>NotificationTestSendAdminService</c>) y el endpoint de canales
/// (<c>NotificationChannelsAdminService</c>) — ver
/// <c>ExplicitChannelEmailSenderRegistrationTests.IExplicitChannelEmailSender_HasNoConsumersOutsideTheKnownBaseline</c>,
/// que falla si aparece un tercer consumidor en el ensamblado. Deliberadamente NO se añade un campo "canal forzado" a
/// <see cref="EmailMessage"/>: ese contrato lo usan los 6 puntos de envío de producción, y un campo
/// así sería una puerta trasera para saltarse la configuración del tenant sin querer. Esta interfaz
/// vive aparte, y solo la implementa <see cref="TenantChannelEmailRouter"/> — el mismo enrutador,
/// con la MISMA regla de disponibilidad que <see cref="IEmailSender.SendAsync"/> usa para el camino
/// normal (ver <see cref="IsChannelAvailable"/>).
/// </remarks>
internal interface IExplicitChannelEmailSender
{
    /// <summary>
    /// Envía por <paramref name="channel"/> SIN consultar la política del tenant y SIN el bypass de
    /// correos de cuenta (AC3): quien llama ya decidió el canal explícitamente. <c>TenantApi</c> sin
    /// adaptador registrado en este ambiente ⇒ <see cref="EmailSendOutcome.ConfigurationIncomplete"/> —
    /// mismo criterio que el camino normal, nunca cae a SMTP en silencio.
    /// </summary>
    Task<EmailSendResult> SendAsync(NotificationChannel channel, EmailMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// <c>true</c> si <paramref name="channel"/> tiene lo necesario para enviar en este ambiente, con
    /// la MISMA regla que usa <see cref="SendAsync(NotificationChannel, EmailMessage, CancellationToken)"/>
    /// (no una copia divergente). <c>FlitSmtp</c> siempre está disponible (el transporte, consola o
    /// SMTP real, siempre está registrado); <c>TenantApi</c> depende de si el adaptador Renting está
    /// registrado en este ambiente (mismo interruptor que decide el <c>ConfigurationIncomplete</c>).
    /// </summary>
    bool IsChannelAvailable(NotificationChannel channel);
}

/// <summary>
/// HU #11362 (Feature #11348, cierra Bug #11311) — enruta cada envío según el canal configurado del
/// tenant (<c>admin.tenant_operational_policies.notification_channel</c>), ANTES de que
/// <see cref="Flit.Infrastructure.Notifications.DeliveryLog.NotificationDeliveryLoggingEmailSender"/>
/// (HU #11363) envuelva el resultado en la bitácora: esta clase se registra como el "concreteSender"
/// interior del decorador, no lo sustituye ni lo reordena — la duración medida por el decorador sigue
/// incluyendo el enrutamiento y el envío real.
/// </summary>
/// <remarks>
/// <para>
/// <b>AC3 — los correos de cuenta ignoran el canal.</b> Los cuatro disparadores de cuenta
/// (invitación, reenvío de invitación, recuperación de contraseña, reset administrativo) comparten
/// las tres plantillas del <see cref="NotificationModule.Security"/> del catálogo
/// (<see cref="NotificationTemplateCatalog"/>) y SIEMPRE salen por <see cref="_flitTransport"/> (el
/// SMTP/consola de FLIT), sin importar el canal del tenant: es decisión de producto — elimina el
/// punto único de fallo de un tercero en la ruta de acceso a la plataforma. Cualquier plantilla que
/// NO resuelva a <see cref="NotificationModule.Security"/> (hoy, las de
/// <see cref="NotificationModule.Analytics"/>) se enruta por el canal (AC1/AC2/AC6). Este bypass
/// SOLO aplica al método de una vía (<see cref="SendAsync(EmailMessage, CancellationToken)"/>) —
/// <see cref="SendAsync(NotificationChannel, EmailMessage, CancellationToken)"/> (HU #11371) no lo
/// aplica NUNCA: el llamador ya decidió el canal a propósito.
/// </para>
/// <para>
/// <b>AC1/AC2/AC6 — resolución por canal.</b> Sin tenant resoluble, o tenant sin política operativa,
/// o canal <see cref="NotificationChannel.FlitSmtp"/> ⇒ <see cref="_flitTransport"/>, con el
/// remitente de la configuración SMTP (AC2/AC6). Canal <see cref="NotificationChannel.TenantApi"/>
/// con el canal Renting habilitado ⇒ <see cref="IRentingEmailApiSender"/>, con el remitente
/// configurado para ESE canal (<see cref="RentingChannelOptions.SendEmailSenderEmail"/> /
/// <see cref="RentingChannelOptions.SendEmailSenderUsername"/>, AC1). Canal
/// <see cref="NotificationChannel.TenantApi"/> con el canal Renting NO habilitado (o no registrado
/// — mismo interruptor que <c>InfrastructureExtensions.AddRentingChannel</c>) ⇒
/// <see cref="EmailSendOutcome.ConfigurationIncomplete"/> — NUNCA cae a SMTP en silencio: eso
/// mandaría por FLIT un correo que el tenant pidió que saliera por su propia API (caso heredado de la
/// HU #11359 AC6).
/// </para>
/// <para>
/// <b>HU #11371 — registro en DI.</b> Esta clase se registra como servicio propio (<c>Scoped</c>,
/// bajo su tipo concreto) en vez de construirse inline dentro de la fábrica de <see cref="IEmailSender"/>:
/// así el banco de pruebas puede alcanzarla a través de <see cref="IExplicitChannelEmailSender"/> sin
/// que el pipeline de producción cambie — <see cref="Flit.Infrastructure.Notifications.DeliveryLog.NotificationDeliveryLoggingEmailSender"/>
/// sigue envolviendo esta misma instancia como su "inner" para los 6 puntos de envío de producción.
/// </para>
/// </remarks>
internal sealed partial class TenantChannelEmailRouter(
    IEmailSender flitTransport,
    ITenantSettingsRepository tenantSettingsRepository,
    IRentingEmailApiSender? rentingEmailApiSender,
    IOptions<RentingChannelOptions> rentingOptions,
    ILogger<TenantChannelEmailRouter> logger) : IEmailSender, IExplicitChannelEmailSender
{
    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (IsAccountEmail(message.TemplateKey))
        {
            // AC3 — ignora el canal por completo.
            return await flitTransport.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }

        var channel = await ResolveChannelAsync(message.TenantId, cancellationToken).ConfigureAwait(false);
        // HU #11372 — camino de producción: NUNCA propaga la exención de buzón controlado. No hay
        // parámetro ni rama por la que pudiera colarse — este método sencillamente llama a
        // SendViaChannelAsync sin ese argumento, así que el desvío de la HU #11364 sigue aplicando
        // siempre para los 6 puntos de envío reales.
        return await SendViaChannelAsync(channel, message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// HU #11371 — capacidad del banco de pruebas: envía por <paramref name="channel"/> tal cual fue
    /// solicitado, sin resolver la política del tenant ni aplicar el bypass de correos de cuenta (AC3
    /// no aplica aquí a propósito).
    /// </summary>
    /// <remarks>
    /// HU #11372 (decisión del PO 2026-08-11) — ÚNICO camino que propaga
    /// <see cref="ControlledMailboxRecipient"/>: el destinatario de <paramref name="message"/> no es
    /// dato de un cliente final, es el buzón único que un SuperAdmin configuró explícitamente en la
    /// UI del banco de pruebas de notificaciones. Ver <see cref="ControlledMailboxRecipient"/> para
    /// la explicación completa de por qué esto no debilita el desvío obligatorio de la HU #11364.
    /// </remarks>
    public Task<EmailSendResult> SendAsync(NotificationChannel channel, EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return SendViaChannelAsync(channel, message, cancellationToken, ControlledMailboxRecipient.Instance);
    }

    /// <summary>
    /// HU #11371 — MISMA regla que decide <see cref="SendViaChannelAsync"/>: <c>FlitSmtp</c> siempre
    /// disponible (transporte siempre registrado); <c>TenantApi</c> disponible únicamente cuando el
    /// adaptador Renting está registrado en este ambiente.
    /// </summary>
    public bool IsChannelAvailable(NotificationChannel channel) =>
        channel != NotificationChannel.TenantApi || rentingEmailApiSender is not null;

    private async Task<EmailSendResult> SendViaChannelAsync(
        NotificationChannel channel,
        EmailMessage message,
        CancellationToken cancellationToken,
        ControlledMailboxRecipient? recipientExemption = null)
    {
        if (channel != NotificationChannel.TenantApi)
        {
            // AC2/AC6 — FlitSmtp explícito, o tenant sin política operativa (default).
            return await flitTransport.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }

        if (rentingEmailApiSender is null)
        {
            // Caso heredado de la HU #11359 AC6 — canal solicitado pero NO habilitado por
            // configuración en este ambiente. Nunca se cae a SMTP en silencio.
            LogTenantApiChannelNotAvailable(logger, message.TenantId ?? Guid.Empty, message.TemplateKey);
            return EmailSendResult.Failed(EmailSendOutcome.ConfigurationIncomplete);
        }

        var options = rentingOptions.Value;
        var request = RentingSendEmailRequest.ToSingleRecipient(
            message.Subject,
            message.HtmlBody,
            new RentingEmailAddress(options.SendEmailSenderEmail, options.SendEmailSenderUsername),
            new RentingEmailAddress(message.ToEmail, message.ToName));

        // HU #11372 — solo el camino del banco de pruebas (SendAsync(NotificationChannel, ...))
        // suministra recipientExemption; el camino de producción (SendAsync(EmailMessage, ...)) lo
        // deja en null, así que el desvío de la HU #11364 sigue aplicando siempre para los 6 puntos
        // de envío reales.
        return recipientExemption is not null
            ? await rentingEmailApiSender.SendAsync(request, recipientExemption, cancellationToken).ConfigureAwait(false)
            : await rentingEmailApiSender.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// AC1/AC2/AC6 — sin tenant resoluble en el mensaje (ver <see cref="EmailMessage.TenantId"/>) o
    /// sin fila de política operativa para el tenant, el canal por defecto es
    /// <see cref="NotificationChannel.FlitSmtp"/>: nunca se intenta el canal del cliente sin que el
    /// tenant lo haya configurado explícitamente.
    /// </summary>
    private async Task<NotificationChannel> ResolveChannelAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        if (tenantId is not { } id)
        {
            return NotificationChannel.FlitSmtp;
        }

        var settings = await tenantSettingsRepository.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return settings?.NotificationChannel ?? NotificationChannel.FlitSmtp;
    }

    /// <summary>
    /// AC3 — los 3 templates de <see cref="NotificationModule.Security"/> (invitación —que cubre
    /// crear y reenviar—, recuperación de contraseña, reset administrativo) son los "correos de
    /// cuenta". Una plantilla no catalogada (id desconocido) NO se trata como cuenta: sigue la
    /// resolución por canal — el bypass de AC3 es una excepción explícita, no el comportamiento por
    /// defecto.
    /// </summary>
    private static bool IsAccountEmail(string templateKey) =>
        NotificationTemplateCatalog.TryResolve(templateKey, out var descriptor)
        && descriptor.Module == NotificationModule.Security;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Enrutamiento de notificaciones: el tenant {TenantId} solicita el canal TENANT_API "
            + "para la plantilla {TemplateKey}, pero el canal Renting no está habilitado en este "
            + "ambiente. No se envía por SMTP de FLIT como respaldo silencioso.")]
    private static partial void LogTenantApiChannelNotAvailable(ILogger logger, Guid tenantId, string templateKey);
}
