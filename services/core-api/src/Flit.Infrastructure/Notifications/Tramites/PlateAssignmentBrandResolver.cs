using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Notifications.Tramites;

/// <summary>
/// HU #11486 (ADR-0046) — marca FLIT/Renting por NIT del tenant cliente normalizado.
/// Si la marca difiere del canal configurado, registra warning y prevalece la marca por NIT.
/// </summary>
internal sealed class PlateAssignmentBrandResolver(
    FlitDbContext db,
    INotificationChannelResolver channelResolver,
    ILogger<PlateAssignmentBrandResolver> logger) : IPlateAssignmentBrandResolver
{
    public PlateAssignmentEmailBrand ResolveFromTaxId(string? taxId)
    {
        var normalized = TaxIdNormalizer.NormalizeBase(taxId);
        return string.Equals(
            normalized,
            AsignacionPlacaEmailComposer.RentingCompanyRegisteredNit,
            StringComparison.Ordinal)
            ? PlateAssignmentEmailBrand.Renting
            : PlateAssignmentEmailBrand.Flit;
    }

    public async Task<PlateAssignmentEmailBrand> ResolveForClientTenantAsync(
        Guid clientTenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(clientTenantId, Guid.Empty);

        var tenant = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == clientTenantId)
            .Select(t => new { t.TaxId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var brand = ResolveFromTaxId(tenant?.TaxId);
        var channel = await channelResolver.ResolveAsync(clientTenantId, cancellationToken)
            .ConfigureAwait(false);
        var channelBrand = BrandFromChannel(channel);

        if (brand != channelBrand)
        {
            PlateAssignmentBrandLog.MarcaCanalDiscrepan(
                logger,
                clientTenantId,
                brand,
                channel,
                channelBrand);
        }

        return brand;
    }

    internal static PlateAssignmentEmailBrand BrandFromChannel(NotificationChannel channel) =>
        channel == NotificationChannel.TenantApi
            ? PlateAssignmentEmailBrand.Renting
            : PlateAssignmentEmailBrand.Flit;
}

internal static partial class PlateAssignmentBrandLog
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Asignación placa: marca por NIT ({BrandByNit}) difiere del canal del tenant {TenantId} ({Channel} → {BrandByChannel}); prevalece marca por NIT.")]
    public static partial void MarcaCanalDiscrepan(
        ILogger logger,
        Guid tenantId,
        PlateAssignmentEmailBrand brandByNit,
        NotificationChannel channel,
        PlateAssignmentEmailBrand brandByChannel);
}
