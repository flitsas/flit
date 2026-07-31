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
        settings.PaymentMethods,
        settings.RuntFailoverTimeoutMs,
        ToChoices(settings.ConsultationProviderConfig),
        settings.PlatePreassignEnabled,
        settings.PlateFlowSkipToTerminado,
        new AvaluoProviderConfigDto(
            settings.AvaluoProviderConfig.Primary,
            settings.AvaluoProviderConfig.Enabled),
        settings.FinesQuerySource);

    private static Dictionary<string, ConsultationProviderChoice> ToChoices(
        ConsultationProviderConfig config)
    {
        var result = new Dictionary<string, ConsultationProviderChoice>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, selection) in config.ByKind)
        {
            result[kind] = new ConsultationProviderChoice(selection.Primary, selection.Fallback);
        }

        return result;
    }
}
