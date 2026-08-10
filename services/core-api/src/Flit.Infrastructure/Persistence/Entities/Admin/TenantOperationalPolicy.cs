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

    /// <summary>Bloquea creación de trámites familia TRASPASO.</summary>
    public bool BlockProcedureFamilyTraspaso { get; set; }

    /// <summary>Bloquea creación de trámites familia OTROS.</summary>
    public bool BlockProcedureFamilyOtros { get; set; }

    public bool AllowMiscNewVehicles { get; set; } = true;

    public bool OnlyOwnVehicles { get; set; }

    /// <summary>Solo vehículos propios — familia MATRICULAS.</summary>
    public bool OnlyOwnVehiclesMatriculas { get; set; }

    /// <summary>Solo vehículos propios — familia OTROS.</summary>
    public bool OnlyOwnVehiclesOtros { get; set; }

    public bool SignatureVaultEnabled { get; set; }

    /// <summary>Preasignación de placa activa (Feature #10587).</summary>
    public bool PlatePreassignEnabled { get; set; }

    /// <summary>
    /// Con placa completa/rango al radicar, omite el paso gestor (Asignado) y aterriza en Terminado.
    /// No afecta la ruta Sin asignar (preasignación por dígito).
    /// </summary>
    public bool PlateFlowSkipToTerminado { get; set; }

    /// <summary>
    /// Al procesar en sub-estado asignado se consulta el SOAT en el RUNT y, sin SOAT vigente, el
    /// avance se detiene. Desactivada, el hallazgo solo se informa y el tramite continua.
    /// </summary>
    public bool ValidateSoatWithRunt { get; set; }

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

    /// <summary>
    /// Fuente de la consulta de comparendos de la compañía (FEATURE 02): <c>internal</c>
    /// (módulo de comparendos con fuente base cargada) o <c>external</c> (consulta en línea al
    /// SIMIT). Se persiste y audita aquí; su USO en el flujo del trámite es FEATURE 05.
    /// </summary>
    public string FinesQuerySource { get; set; } = "external";

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
