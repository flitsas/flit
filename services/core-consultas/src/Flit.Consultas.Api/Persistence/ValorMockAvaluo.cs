namespace Flit.Consultas.Api.Persistence;

/// <summary>
/// Valor de avalúo de prueba para el modo mock de los proveedores (HU #13348; antes <c>tramites.avaluo_mock_values</c>).
/// Solo se siembra en DEV/QA: en producción la tabla queda vacía y un proveedor en mock responde «sin datos».
/// </summary>
public sealed class ValorMockAvaluo
{
    public Guid Id { get; set; }

    /// <summary>VIN o placa normalizado en mayúsculas.</summary>
    public string Clave { get; set; } = string.Empty;

    /// <summary>Fuente: <c>fasecolda</c> | <c>base_gravable</c> | <c>mercado_libre</c>.</summary>
    public string Fuente { get; set; } = string.Empty;

    /// <summary>Valor de referencia en pesos colombianos (ya convertido, no en miles).</summary>
    public decimal ValorCop { get; set; }
}
