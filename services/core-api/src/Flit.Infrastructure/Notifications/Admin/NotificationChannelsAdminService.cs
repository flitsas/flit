using Flit.Admin.Application.Companies.Settings;
using Flit.Admin.Application.Plataforma.Notificaciones;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Email;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Infrastructure.Notifications.Routing;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Notifications.Admin;

/// <summary>
/// Resuelve el remitente de cada canal de notificaciones LEYENDO configuración (HU #11367,
/// Feature #11349) — <see cref="EmailSettings"/> para <c>FLIT_SMTP</c> y
/// <see cref="RentingChannelOptions"/> para <c>TENANT_API</c>. Deliberadamente NO construye
/// ningún adaptador ni instancia nada que envíe correo: por eso el Feature #11349 se mantiene
/// independiente del #11348 (ver <c>IEmailSender</c>).
/// </summary>
/// <remarks>
/// HU #11371 — <c>IsConfigured</c> de <c>TENANT_API</c> se alinea con
/// <see cref="IExplicitChannelEmailSender.IsChannelAvailable"/>: la MISMA regla que decide si el
/// banco de pruebas puede enviar por ese canal (adaptador Renting registrado en este ambiente), en
/// vez de una copia divergente basada solo en si <see cref="RentingChannelOptions.SendEmailSenderEmail"/>
/// / <see cref="RentingChannelOptions.SendEmailSenderUsername"/> están pobladas. Antes de esta HU
/// podían divergir: un ambiente con esas dos variables pobladas pero el canal apagado
/// (<c>RENTING_API_ENABLED=false</c>) informaba "configurado" mientras el envío de verdad respondía
/// "no disponible". El remitente informativo (<c>SenderEmail</c>/<c>SenderName</c>) sigue viniendo
/// de la configuración, sin cambios.
/// </remarks>
internal sealed class NotificationChannelsAdminService(
    EmailSettings emailSettings,
    IOptions<RentingChannelOptions> rentingOptions,
    IExplicitChannelEmailSender explicitChannelSender) : INotificationChannelsAdminService
{
    private const string LabelFlitSmtp = "Colas FLIT";
    private const string LabelTenantApi = "API Renting cliente";

    private readonly EmailSettings _emailSettings =
        emailSettings ?? throw new ArgumentNullException(nameof(emailSettings));

    private readonly RentingChannelOptions _rentingOptions =
        (rentingOptions ?? throw new ArgumentNullException(nameof(rentingOptions))).Value;

    private readonly IExplicitChannelEmailSender _explicitChannelSender =
        explicitChannelSender ?? throw new ArgumentNullException(nameof(explicitChannelSender));

    public Task<IReadOnlyList<NotificationChannelView>> GetAsync(CancellationToken ct = default)
    {
        var flitSenderEmail = NullIfBlank(_emailSettings.DefaultSenderEmail);
        var flitSenderName = NullIfBlank(_emailSettings.DefaultSenderName);

        var rentingSenderEmail = NullIfBlank(_rentingOptions.SendEmailSenderEmail);
        var rentingSenderName = NullIfBlank(_rentingOptions.SendEmailSenderUsername);

        IReadOnlyList<NotificationChannelView> channels =
        [
            new NotificationChannelView(
                Channel: SettingsWire.ChannelFlitSmtp,
                Label: LabelFlitSmtp,
                IsDefault: true,
                IsConfigured: flitSenderEmail is not null,
                SenderEmail: flitSenderEmail,
                SenderName: flitSenderName),
            new NotificationChannelView(
                Channel: SettingsWire.ChannelTenantApi,
                Label: LabelTenantApi,
                IsDefault: false,
                IsConfigured: _explicitChannelSender.IsChannelAvailable(NotificationChannel.TenantApi),
                SenderEmail: rentingSenderEmail,
                SenderName: rentingSenderName),
        ];

        return Task.FromResult(channels);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
