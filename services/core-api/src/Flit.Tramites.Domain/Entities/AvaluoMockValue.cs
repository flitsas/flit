namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Valor de referencia de avalúo por VIN o placa y fuente (Feature #10707).
/// En DEV/QA se siembra con datos de prueba (migración gated por entorno) para habilitar
/// el modo mock de los proveedores de avalúo sin credenciales reales. En producción la tabla
/// arranca vacía; los proveedores en modo <c>real</c> no la consultan.
/// </summary>
public sealed class AvaluoMockValue
{
    public Guid Id { get; set; }

    /// <summary>VIN o placa normalizado en mayúsculas.</summary>
    public string MatchKey { get; set; } = string.Empty;

    /// <summary>Fuente: <c>fasecolda</c> | <c>base_gravable</c> | <c>mercado_libre</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Valor de referencia en pesos colombianos (ya convertido, no en miles).</summary>
    public decimal ValueCop { get; set; }
}
