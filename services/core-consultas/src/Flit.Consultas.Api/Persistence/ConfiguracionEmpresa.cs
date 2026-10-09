namespace Flit.Consultas.Api.Persistence;

/// <summary>
/// Configuración de consultas de una empresa (ADR-0065, HU #13343/#13344): cadena de proveedores por tipo de consulta,
/// presupuesto de failover, fuente de comparendos y proveedores de avalúo. Mismos formatos JSON que
/// <c>admin.tenant_operational_policies</c>, de donde se migra (HU #13344). Sin fila = los valores por defecto.
/// </summary>
public sealed class ConfiguracionEmpresa
{
    public Guid TenantId { get; set; }

    /// <summary><c>{"vehicle_plate": {"primary": "kyverum_runt", "fallback": ["verifik"]}, …}</c>.</summary>
    public string? CadenasJson { get; set; }

    public int? FailoverTimeoutMs { get; set; }

    /// <summary><c>internal</c> | <c>external</c> (FinesSourceCodes).</summary>
    public string FuenteMultas { get; set; } = "external";

    /// <summary><c>{"primary": "fasecolda", "enabled": ["fasecolda", …]}</c>.</summary>
    public string? AvaluosJson { get; set; }

    public DateTimeOffset ActualizadoEn { get; set; }
}
