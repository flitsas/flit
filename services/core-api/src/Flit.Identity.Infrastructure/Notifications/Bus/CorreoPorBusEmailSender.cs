using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications.Catalog;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Notificaciones;
using Flit.Modules.Security.Domain.Auth;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Notifications.Bus;

/// <summary>
/// HU #13355/#13359 (Epic #13316) — el <see cref="IEmailSender"/> de core-api y core-identity no envía: resuelve el canal
/// con la regla de siempre (los correos de cuenta, siempre por FLIT; el resto, el canal de la empresa) y deja el correo
/// YA ARMADO como trabajo <c>notificaciones.email.send</c> en la outbox, en su propia transacción. Responde «enviado» al
/// quedar encolado: el envío, los reintentos y el registro de entregas los hace core-notificaciones. Si no se puede
/// encolar (base caída), responde proveedor no disponible y el flujo reintenta como hoy. Un correo sin empresa
/// (simulación de mandato, usuario sin rol, reportes de alcance SuperAdmin) va con la empresa
/// <see cref="PlatformTenants.Plataforma"/> y por FLIT: un trabajo siempre es de una empresa (contrato §7).
/// </summary>
internal sealed partial class CorreoPorBusEmailSender(
    INotificationChannelResolver canales,
    IServiceScopeFactory scopes,
    ILogger<CorreoPorBusEmailSender> logger) : IEmailSender
{
    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var tenantId = PlatformTenants.O(message.TenantId);
        var canal = EsCorreoDeCuenta(message.TemplateKey) || tenantId == PlatformTenants.Plataforma
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

    /// <summary>
    /// Correos de cuenta (módulo Seguridad: invitación, recuperación, …): siempre por FLIT, sin importar la política de
    /// la empresa (HU #11359 AC3).
    /// </summary>
    internal static bool EsCorreoDeCuenta(string templateKey) =>
        NotificationTemplateCatalog.TryResolve(templateKey, out var descriptor)
        && descriptor.Module == NotificationModule.Security;

    [LoggerMessage(EventId = 7521, Level = LogLevel.Information, Message = "Correo {Plantilla} encolado para Notificaciones (trabajo {EventId})")]
    private static partial void LogEncolado(ILogger logger, string plantilla, Guid eventId);

    [LoggerMessage(EventId = 7522, Level = LogLevel.Error, Message = "No se pudo encolar el correo {Plantilla} para Notificaciones; el flujo lo reintenta")]
    private static partial void LogNoEncolado(ILogger logger, string plantilla, Exception ex);
}
