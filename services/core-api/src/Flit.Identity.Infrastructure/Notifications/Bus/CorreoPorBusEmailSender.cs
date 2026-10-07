using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Notificaciones;
using Flit.Modules.Security.Domain.Auth;
using Flit.Platform.Sdk.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Notifications.Bus;

/// <summary>
/// HU #13355 (Epic #13316) — con <see cref="NotificacionesRemoto.FlagKey"/> encendida, el <see cref="IEmailSender"/> de
/// core-api y core-identity deja de enviar: resuelve el canal con la regla de siempre (los correos de cuenta, siempre por
/// FLIT; el resto, el canal de la empresa) y deja el correo YA ARMADO como trabajo <c>notificaciones.email.send</c> en la
/// outbox, en su propia transacción. Responde «enviado» al quedar encolado: el envío, los reintentos y el registro de
/// entregas los hace core-notificaciones. Si no se puede encolar (base caída), responde proveedor no disponible y el
/// flujo reintenta como hoy. Un correo sin empresa (la simulación de mandato) sale en proceso: un trabajo es de una
/// empresa (contrato §7).
/// </summary>
internal sealed partial class CorreoPorBusEmailSender(
    IEmailSender enProceso,
    INotificationChannelResolver canales,
    IServiceScopeFactory scopes,
    ILogger<CorreoPorBusEmailSender> logger) : IEmailSender
{
    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.TenantId is not { } tenantId || tenantId == Guid.Empty)
            return await enProceso.SendAsync(message, cancellationToken).ConfigureAwait(false);

        var canal = TenantChannelEmailRouter.IsAccountEmail(message.TemplateKey)
            ? CanalCorreo.FlitSmtp
            : await canales.ResolveAsync(tenantId, cancellationToken).ConfigureAwait(false) == NotificationChannel.TenantApi
                ? CanalCorreo.EmpresaApi
                : CanalCorreo.FlitSmtp;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var trabajo = scope.ServiceProvider.GetRequiredService<IPlatformOutbox>()
                .EnqueueJob(TrabajoCorreo.Tipo, 1, tenantId, TrabajoCorreo.De(message, canal));
            await scope.ServiceProvider.GetRequiredService<IIdentityDb>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            LogEncolado(logger, message.TemplateKey, trabajo.EventId);
            return new EmailSendResult(true, EmailSendOutcome.Sent, "Encolado en Notificaciones.") { Channel = CanalCorreoCodigos.De(canal) };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNoEncolado(logger, message.TemplateKey, ex);
            return EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable) with { Channel = CanalCorreoCodigos.De(canal) };
        }
    }

    [LoggerMessage(EventId = 7521, Level = LogLevel.Information, Message = "Correo {Plantilla} encolado para Notificaciones (trabajo {EventId})")]
    private static partial void LogEncolado(ILogger logger, string plantilla, Guid eventId);

    [LoggerMessage(EventId = 7522, Level = LogLevel.Error, Message = "No se pudo encolar el correo {Plantilla} para Notificaciones; el flujo lo reintenta")]
    private static partial void LogNoEncolado(ILogger logger, string plantilla, Exception ex);
}

/// <summary>Bandera de Notificaciones remoto (HU #13355), común a core-api y core-identity.</summary>
public static class NotificacionesRemoto
{
    public const string FlagKey = "Notificaciones:Remoto:Habilitado";
}
