using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications.Catalog;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Notifications.Routing;

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
/// <see cref="NotificationModule.Analytics"/>) se enruta por el canal (AC1/AC2/AC6).
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
/// </remarks>
internal sealed partial class TenantChannelEmailRouter(
    IEmailSender flitTransport,
    ITenantSettingsRepository tenantSettingsRepository,
    IRentingEmailApiSender? rentingEmailApiSender,
    IOptions<RentingChannelOptions> rentingOptions,
    ILogger<TenantChannelEmailRouter> logger) : IEmailSender
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
        if (channel != NotificationChannel.TenantApi)
        {
            // AC2/AC6 — FlitSmtp explícito, o tenant sin política operativa (default).
            return await flitTransport.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }

        if (rentingEmailApiSender is null)
        {
            // Caso heredado de la HU #11359 AC6 — canal solicitado por el tenant pero NO habilitado
            // por configuración en este ambiente. Nunca se cae a SMTP en silencio. TenantId no puede
            // ser null aquí: ResolveChannelAsync solo devuelve TenantApi cuando resolvió una fila de
            // política para un tenant concreto.
            LogTenantApiChannelNotAvailable(logger, message.TenantId ?? Guid.Empty, message.TemplateKey);
            return EmailSendResult.Failed(EmailSendOutcome.ConfigurationIncomplete);
        }

        var options = rentingOptions.Value;
        var request = RentingSendEmailRequest.ToSingleRecipient(
            message.Subject,
            message.HtmlBody,
            new RentingEmailAddress(options.SendEmailSenderEmail, options.SendEmailSenderUsername),
            new RentingEmailAddress(message.ToEmail, message.ToName));

        return await rentingEmailApiSender.SendAsync(request, cancellationToken).ConfigureAwait(false);
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
