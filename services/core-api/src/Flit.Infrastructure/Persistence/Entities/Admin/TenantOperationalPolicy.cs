namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Configuración operativa por tenant — <c>admin.tenant_operational_policies</c>
/// (HU #10154 DDL, gestionada por HU #10190). RLS por <c>app.current_tenant_id</c>.
/// </summary>
public sealed class TenantOperationalPolicy
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public bool AllowInitialRegistration { get; set; }

    public bool AllowMiscNewVehicles { get; set; } = true;

    public bool OnlyOwnVehicles { get; set; }

    public bool SignatureVaultEnabled { get; set; }

    public string NotificationChannel { get; set; } = "flit_smtp";

    public string NotificationTarget { get; set; } = "submitter";

    /// <summary>Array JSON (jsonb) de métodos de recaudo. Ej: <c>["pse","efecty"]</c>.</summary>
    public string PaymentMethods { get; set; } = "[]";

    public string RuntProviderStrategy { get; set; } = "verifik";

    public int RuntFailoverTimeoutMs { get; set; } = 4000;

    /// <summary>
    /// Override por tenant de la cadena de proveedores de consulta RUNT (jsonb, HU #10478).
    /// Forma: <c>{ "vehicle_vin": { "primary": "kyverum_runt", "fallback": ["verifik"] }, ... }</c>.
    /// <c>'{}'</c> (default) = usar los defaults globales de <c>Consultations:DefaultChains</c>.
    /// </summary>
    public string ConsultationProviderConfig { get; set; } = "{}";

    /// <summary>
    /// Proveedores de avalúo comercial habilitados por tenant (jsonb, Feature #10707).
    /// Forma: <c>{ "primary": "fasecolda", "enabled": ["fasecolda","mercado_libre"] }</c>.
    /// <c>'{}'</c> (default) = solo Fasecolda (proveedor base).
    /// </summary>
    public string AvaluoProviderConfig { get; set; } = "{}";

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
