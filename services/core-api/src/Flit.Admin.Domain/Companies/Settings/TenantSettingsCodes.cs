namespace Flit.Admin.Domain.Companies.Settings;

/// <summary>
/// Conversión entre los enums de dominio y su representación persistida en BD
/// (<c>admin.tenant_operational_policies</c>). El canal se guarda en snake_case
/// minúsculas; el destinatario en mayúsculas (según AC y DDL).
/// </summary>
public static class TenantSettingsCodes
{
    public const string ChannelFlitSmtp = "flit_smtp";
    public const string ChannelTenantApi = "tenant_api";

    public const string TargetComprador = "COMPRADOR";
    public const string TargetRadicador = "RADICADOR";
    public const string TargetNinguno = "NINGUNO";

    /// <summary>Alias heredado del DDL (<c>notification_target DEFAULT 'submitter'</c>).</summary>
    private const string TargetLegacySubmitter = "submitter";

    public static string ToDb(NotificationChannel channel) => channel switch
    {
        NotificationChannel.FlitSmtp => ChannelFlitSmtp,
        NotificationChannel.TenantApi => ChannelTenantApi,
        _ => ChannelFlitSmtp,
    };

    public static string ToDb(NotificationTarget target) => target switch
    {
        NotificationTarget.Comprador => TargetComprador,
        NotificationTarget.Radicador => TargetRadicador,
        NotificationTarget.Ninguno => TargetNinguno,
        _ => TargetNinguno,
    };

    public static NotificationChannel ParseChannelDb(string value) => value switch
    {
        ChannelFlitSmtp => NotificationChannel.FlitSmtp,
        ChannelTenantApi => NotificationChannel.TenantApi,
        _ => NotificationChannel.FlitSmtp,
    };

    public static NotificationTarget ParseTargetDb(string value) => value switch
    {
        TargetComprador => NotificationTarget.Comprador,
        TargetRadicador or TargetLegacySubmitter => NotificationTarget.Radicador,
        TargetNinguno => NotificationTarget.Ninguno,
        _ => NotificationTarget.Radicador,
    };
}
