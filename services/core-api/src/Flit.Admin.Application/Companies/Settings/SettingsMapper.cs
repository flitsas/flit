using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.Settings;

/// <summary>Proyección del modelo de dominio a la respuesta API.</summary>
internal static class SettingsMapper
{
    public static TenantSettingsResponse ToResponse(TenantSettings settings) => new(
        settings.TenantId,
        new SwitchesMatricula(
            settings.AllowInitialRegistration,
            settings.AllowMiscNewVehicles,
            settings.OnlyOwnVehicles),
        settings.SignatureVaultEnabled,
        SettingsWire.ToWire(settings.NotificationChannel),
        SettingsWire.ToWire(settings.NotificationTarget),
        settings.PaymentMethods);
}
