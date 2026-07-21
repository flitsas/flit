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
    // FEATURE 02 — política "solo vehículos propios" del tenant, expuesta al wizard para adaptar la
    // captura del propietario en el paso de consulta (autorelleno del NIT del tenant). Default false.
    bool OnlyOwnVehicles = false,
    // FEATURE 05 — fuente de la consulta de comparendos del tenant (internal|external), creada en
    // FEATURE 02 y consumida por FinesProviderResolver. Viaja aquí y no en un puerto propio porque
    // el preflight YA lee este override una vez por corrida: cero lecturas extra a la base.
    // Precedente: OnlyOwnVehicles tampoco es una cadena de proveedores.
    string FinesQuerySource = FinesSourceCodes.External);

/// <summary>Proveedor primario + orden de fallback para un tipo de consulta (clave: vehicle_vin|vehicle_plate|conductor).</summary>
public sealed record ConsultationChainSelection(string Primary, IReadOnlyList<string> Fallback);
