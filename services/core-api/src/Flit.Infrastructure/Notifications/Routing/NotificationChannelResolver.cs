using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Infrastructure.Notifications.Routing;

/// <summary>
/// HU #11466 (Feature #11459, ADR-0045) — resolución compartida del canal de notificación del
/// tenant. La usan el enrutador de transporte (<see cref="TenantChannelEmailRouter"/>) y, más
/// adelante, el worker/composer del aviso de trámite: sin un único punto, un tenant Renting podría
/// recibir cuerpo FLIT por la API de Renting.
/// </summary>
internal interface INotificationChannelResolver
{
    /// <summary>
    /// Sin tenant resoluble o sin política operativa ⇒ <see cref="NotificationChannel.FlitSmtp"/>.
    /// </summary>
    Task<NotificationChannel> ResolveAsync(Guid? tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// Implementación canónica: lee <c>admin.tenant_operational_policies.notification_channel</c>.
/// Comportamiento idéntico al antiguo <c>TenantChannelEmailRouter.ResolveChannelAsync</c> privado.
/// </summary>
internal sealed class NotificationChannelResolver(ITenantSettingsRepository tenantSettingsRepository)
    : INotificationChannelResolver
{
    public async Task<NotificationChannel> ResolveAsync(
        Guid? tenantId, CancellationToken cancellationToken)
    {
        if (tenantId is not { } id)
        {
            return NotificationChannel.FlitSmtp;
        }

        var settings = await tenantSettingsRepository.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return settings?.NotificationChannel ?? NotificationChannel.FlitSmtp;
    }
}
