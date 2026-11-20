namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Cadenas de proveedores de consulta por <see cref="ConsultationKind"/> (HU #10478), enlazadas desde
/// la sección <c>Consultations</c> de appsettings. Kyverum-first con fallback a Verifik. Si la config
/// no trae la cadena de un kind, se usa el default embebido <see cref="Fallback"/> (mismo orden que el
/// plan). El override granular por tenant (jsonb) llega en Fase 4; aquí solo viven los defaults
/// globales. <see cref="FailoverTimeoutMs"/> replica el default de la columna
/// <c>admin.tenant_operational_policies.runt_failover_timeout_ms</c>.
/// </summary>
public sealed class ConsultationChainOptions
{
    public const string SectionName = "Consultations";

    /// <summary>key ∈ {vehicle_vin, vehicle_plate, conductor} → orden de provider keys a intentar.</summary>
    public Dictionary<string, List<string>> DefaultChains { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Presupuesto (ms) para el proveedor primario antes de caer al fallback. Default 4000 (DDL).</summary>
    public int FailoverTimeoutMs { get; set; } = 60_000;

    private static readonly Dictionary<string, string[]> Fallback =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [ConsultationKindKeys.VehicleVin] = ["kyverum_runt", "verifik"],
            [ConsultationKindKeys.VehiclePlate] = ["kyverum_runt", "verifik"],
            // HU #14642: tenant_manual_conductor encabeza la cadena de conductor; el decorador de
            // cortocircuito lo intercepta en core-api ANTES de llamar a core-consultas, por lo que
            // este proveedor nunca llega al registro remoto (CategoriaDe → Desconocida).
            [ConsultationKindKeys.Conductor] = ["tenant_manual_conductor", "kyverum_runt_conductor", "verifik_conductor"],
        };

    /// <summary>Cadena efectiva para una clave de kind: config si existe y no vacía; si no, el default embebido.</summary>
    public IReadOnlyList<string> ChainFor(string kindKey)
    {
        if (DefaultChains.TryGetValue(kindKey, out var chain) && chain.Count > 0)
            return chain;

        return Fallback.TryGetValue(kindKey, out var fallback) ? fallback : [];
    }
}
