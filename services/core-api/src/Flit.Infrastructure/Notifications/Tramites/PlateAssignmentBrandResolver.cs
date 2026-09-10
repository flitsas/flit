using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Tramites.Application.Notifications;

namespace Flit.Infrastructure.Notifications.Tramites;

/// <summary>
/// Marca FLIT/Renting del correo de asignación de placa = canal configurado del tenant cliente
/// (misma regla que <c>ProcedureStateChangeEmailDispatchProcessor</c> para aprobado/rechazado).
/// </summary>
internal sealed class PlateAssignmentBrandResolver(
    INotificationChannelResolver channelResolver) : IPlateAssignmentBrandResolver
{
    public async Task<PlateAssignmentEmailBrand> ResolveForClientTenantAsync(
        Guid clientTenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(clientTenantId, Guid.Empty);

        var channel = await channelResolver.ResolveAsync(clientTenantId, cancellationToken)
            .ConfigureAwait(false);
        return BrandFromChannel(channel);
    }

    internal static PlateAssignmentEmailBrand BrandFromChannel(NotificationChannel channel) =>
        channel == NotificationChannel.TenantApi
            ? PlateAssignmentEmailBrand.Renting
            : PlateAssignmentEmailBrand.Flit;
}
