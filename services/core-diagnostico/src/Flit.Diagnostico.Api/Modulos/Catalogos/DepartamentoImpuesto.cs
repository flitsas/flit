namespace Flit.Diagnostico.Api.Modulos.Catalogos;

/// <summary>
/// Si core-consultas soporta el impuesto vehicular de un departamento (<c>diagnostico.departamentos_impuesto</c>, HU #13723, plan
/// §5, R-9, R-23): caché global del rpc de capacidad, sin empresa (excepción de ADR-0072) y sin edición manual.
/// </summary>
public sealed class DepartamentoImpuesto
{
    public required string DepartamentoCodigo { get; init; }

    public bool Soportado { get; set; }

    /// <summary>Última vez que se refrescó desde core-consultas.</summary>
    public DateTimeOffset ActualizadoEn { get; set; }
}
