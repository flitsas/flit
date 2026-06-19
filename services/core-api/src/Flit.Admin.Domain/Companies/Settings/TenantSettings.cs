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
    /// Configuración por defecto coherente con el DDL
    /// (07-HU10154-admin-tenants.sql) cuando aún no existe fila para el tenant.
    /// </summary>
    public static TenantSettings Default(Guid tenantId) => new()
    {
        TenantId = tenantId,
        AllowInitialRegistration = true,
        AllowMiscNewVehicles = true,
        OnlyOwnVehicles = false,
        SignatureVaultEnabled = false,
        NotificationChannel = NotificationChannel.FlitSmtp,
        NotificationTarget = NotificationTarget.Radicador,
        PaymentMethods = [],
    };
}
