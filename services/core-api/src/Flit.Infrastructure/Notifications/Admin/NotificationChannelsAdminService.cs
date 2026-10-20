using Flit.Admin.Application.Companies.Settings;
using Flit.Admin.Application.Plataforma.Notificaciones;
using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Infrastructure.Notifications.Admin;

/// <summary>
/// Canales de notificación con su remitente (HU #11367, Feature #11349). Desde el corte (HU #13359) los transportes viven
/// en core-notificaciones: el remitente y si el canal está disponible (HU #11371: la MISMA regla con la que se puede
/// enviar por él) vienen de allá. Si Notificaciones no responde, los dos canales salen sin configurar.
/// </summary>
internal sealed class NotificationChannelsAdminService(ICanalesDeNotificaciones canales) : INotificationChannelsAdminService
{
    private const string LabelFlitSmtp = "Colas FLIT";
    private const string LabelTenantApi = "API Renting cliente";

    public async Task<IReadOnlyList<NotificationChannelView>> GetAsync(CancellationToken ct = default)
    {
        IReadOnlyList<CanalDeNotificacion> lista;
        try
        {
            lista = await canales.ListarAsync(ct).ConfigureAwait(false);
        }
        catch (NotificacionesNoDisponibleException)
        {
            lista = [];
        }

        var flit = lista.FirstOrDefault(c => c.Canal == NotificationChannel.FlitSmtp);
        var empresa = lista.FirstOrDefault(c => c.Canal == NotificationChannel.TenantApi);
        return
        [
            new NotificationChannelView(
                Channel: SettingsWire.ChannelFlitSmtp,
                Label: LabelFlitSmtp,
                IsDefault: true,
                IsConfigured: flit?.RemitenteEmail is not null,
                SenderEmail: flit?.RemitenteEmail,
                SenderName: flit?.RemitenteNombre),
            new NotificationChannelView(
                Channel: SettingsWire.ChannelTenantApi,
                Label: LabelTenantApi,
                IsDefault: false,
                IsConfigured: empresa?.Disponible == true,
                SenderEmail: empresa?.RemitenteEmail,
                SenderName: empresa?.RemitenteNombre),
        ];
    }
}
