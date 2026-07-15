namespace Flit.Admin.Domain.Companies.Settings;

/// <summary>
/// Configuración operativa efectiva de un tenant (HU #10190, RF03/RF07–RF10).
/// Modelo de dominio inmutable que mapea <c>admin.tenant_operational_policies</c>.
/// </summary>
public sealed class TenantSettings
{
    /// <summary>Tenant al que pertenece la configuración.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Permite matrícula inicial (<c>allow_initial_registration</c>).</summary>
    public required bool AllowInitialRegistration { get; init; }

    /// <summary>Permite misceláneos de vehículos nuevos (<c>allow_misc_new_vehicles</c>).</summary>
    public required bool AllowMiscNewVehicles { get; init; }

    /// <summary>Restringe a vehículos propios (<c>only_own_vehicles</c>).</summary>
    public required bool OnlyOwnVehicles { get; init; }

    /// <summary>Baúl de firmas activo (<c>signature_vault_enabled</c>).</summary>
    public required bool SignatureVaultEnabled { get; init; }

    /// <summary>Canal de enrutamiento de notificaciones (<c>notification_channel</c>).</summary>
    public required NotificationChannel NotificationChannel { get; init; }

    /// <summary>Destinatario de notificaciones (<c>notification_target</c>).</summary>
    public required NotificationTarget NotificationTarget { get; init; }

    /// <summary>Métodos de recaudo habilitados (<c>payment_methods</c>, jsonb).</summary>
    public required IReadOnlyList<string> PaymentMethods { get; init; }

    /// <summary>
    /// Presupuesto (ms) del proveedor primario de consulta antes de caer al fallback
    /// (<c>runt_failover_timeout_ms</c>, HU #10478). No <c>required</c>: default 60000 (DDL). El RUNT
    /// en frío (vía Kyverum/Verifik) puede tardar decenas de segundos, así que el presupuesto es amplio
    /// para no caer al fallback prematuramente.
    /// </summary>
    public int RuntFailoverTimeoutMs { get; init; } = 60_000;

    /// <summary>
    /// Override por tenant de la cadena de proveedores de consulta RUNT
    /// (<c>consultation_provider_config</c>, jsonb, HU #10478). Vacío = usar defaults globales.
    /// </summary>
    public ConsultationProviderConfig ConsultationProviderConfig { get; init; } = ConsultationProviderConfig.Empty;

    /// <summary>
    /// Proveedores de avalúo comercial habilitados por tenant
    /// (<c>avaluo_provider_config</c>, jsonb, Feature #10707). Default = solo Fasecolda.
    /// </summary>
    public AvaluoProviderConfig AvaluoProviderConfig { get; init; } = AvaluoProviderConfig.Default;

    /// <summary>
    /// Fuente de la consulta de comparendos (<c>fines_query_source</c>, FEATURE 02):
    /// <see cref="TenantSettingsCodes.FinesSourceInternal"/> o
    /// <see cref="TenantSettingsCodes.FinesSourceExternal"/>. Default <c>external</c> (SIMIT en línea).
    /// Su USO en el flujo del trámite llega en FEATURE 05.
    /// </summary>
    public string FinesQuerySource { get; init; } = TenantSettingsCodes.FinesSourceExternal;

    /// <summary>
    /// Configuración por defecto cuando aún no existe fila para el tenant. La matrícula
    /// inicial nace APAGADA (default false): la compañía debe habilitarla explícitamente.
    /// </summary>
    public static TenantSettings Default(Guid tenantId) => new()
    {
        TenantId = tenantId,
        AllowInitialRegistration = false,
        AllowMiscNewVehicles = true,
        OnlyOwnVehicles = false,
        SignatureVaultEnabled = false,
        NotificationChannel = NotificationChannel.FlitSmtp,
        NotificationTarget = NotificationTarget.Radicador,
        PaymentMethods = [],
        RuntFailoverTimeoutMs = 60_000,
        ConsultationProviderConfig = ConsultationProviderConfig.Empty,
        AvaluoProviderConfig = AvaluoProviderConfig.Default,
        FinesQuerySource = TenantSettingsCodes.FinesSourceExternal,
    };
}
