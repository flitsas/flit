using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.Settings;

/// <summary>
/// Conversión entre el formato wire (contrato API público) y los enums de dominio.
/// El canal es case-insensitive y se normaliza con Trim; valores fuera del enum
/// se rechazan (AC2 → 422).
/// </summary>
public static class SettingsWire
{
    public const string ChannelFlitSmtp = "FLIT_SMTP";
    public const string ChannelTenantApi = "TENANT_API";

    public const string TargetComprador = "COMPRADOR";
    public const string TargetRadicador = "RADICADOR";
    public const string TargetNinguno = "NINGUNO";

    public const string AllowedChannels = $"{ChannelFlitSmtp}, {ChannelTenantApi}";
    public const string AllowedTargets = $"{TargetComprador}, {TargetRadicador}, {TargetNinguno}";

    public static bool TryParseChannel(string? value, out NotificationChannel channel)
    {
        switch (Normalize(value))
        {
            case ChannelFlitSmtp:
                channel = NotificationChannel.FlitSmtp;
                return true;
            case ChannelTenantApi:
                channel = NotificationChannel.TenantApi;
                return true;
            default:
                channel = default;
                return false;
        }
    }

    public static bool TryParseTarget(string? value, out NotificationTarget target)
    {
        switch (Normalize(value))
        {
            case TargetComprador:
                target = NotificationTarget.Comprador;
                return true;
            case TargetRadicador:
                target = NotificationTarget.Radicador;
                return true;
            case TargetNinguno:
                target = NotificationTarget.Ninguno;
                return true;
            default:
                target = default;
                return false;
        }
    }

    public static string ToWire(NotificationChannel channel) => channel switch
    {
        NotificationChannel.FlitSmtp => ChannelFlitSmtp,
        NotificationChannel.TenantApi => ChannelTenantApi,
        _ => ChannelFlitSmtp,
    };

    public static string ToWire(NotificationTarget target) => target switch
    {
        NotificationTarget.Comprador => TargetComprador,
        NotificationTarget.Radicador => TargetRadicador,
        NotificationTarget.Ninguno => TargetNinguno,
        _ => TargetNinguno,
    };

    private static string Normalize(string? value) =>
        value?.Trim().ToUpperInvariant() ?? string.Empty;
}
