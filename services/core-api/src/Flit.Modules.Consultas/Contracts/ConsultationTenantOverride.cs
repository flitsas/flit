namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Override por tenant de la cadena de proveedores de consulta (HU #10478, Fase 4). Lo provee el
/// caller (en Fase 5, el handler lo arma desde <c>admin.tenant_operational_policies</c>:
/// <c>consultation_provider_config</c> + <c>runt_failover_timeout_ms</c>). Cuando trae una cadena para
/// un tipo, tiene prioridad sobre los defaults globales; cuando <see cref="Chains"/> o
/// <see cref="FailoverTimeoutMs"/> vienen null, se usan los defaults.
/// </summary>
public sealed record ConsultationTenantOverride(
    IReadOnlyDictionary<string, ConsultationChainSelection>? Chains,
    int? FailoverTimeoutMs,
    // FEATURE 02 — política "solo vehículos propios" familia TRASPASO (legado del wizard).
    bool OnlyOwnVehicles = false,
    // FEATURE 05 — fuente de la consulta de comparendos del tenant (internal|external).
    string FinesQuerySource = FinesSourceCodes.External,
    bool OnlyOwnVehiclesMatriculas = false,
    bool OnlyOwnVehiclesOtros = false,
    /// <summary>Bloqueo de creación: MATRICULAS (⇔ NOT allow_initial_registration).</summary>
    bool BlockProcedureFamilyMatriculas = false,
    bool BlockProcedureFamilyTraspaso = false,
    bool BlockProcedureFamilyOtros = false);

/// <summary>Proveedor primario + orden de fallback para un tipo de consulta (clave: vehicle_vin|vehicle_plate|conductor).</summary>
public sealed record ConsultationChainSelection(string Primary, IReadOnlyList<string> Fallback);
