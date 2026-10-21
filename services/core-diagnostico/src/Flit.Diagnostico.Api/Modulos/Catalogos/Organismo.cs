namespace Flit.Diagnostico.Api.Modulos.Catalogos;

/// <summary>
/// Organismo de tránsito con su departamento (<c>diagnostico.organismos</c>, HU #13723, plan §5, patrón Comparendos HU13459):
/// réplica global de FLIT, sin empresa (excepción de ADR-0072). Con ella el vehículo deriva su departamento del organismo
/// que reporta el RUNT.
/// </summary>
public sealed class Organismo
{
    /// <summary>Código del organismo (clave natural de la réplica).</summary>
    public required string Codigo { get; init; }

    public required string Nombre { get; set; }

    /// <summary>Nombre normalizado para buscar por el <c>transit_office_name</c> del RUNT.</summary>
    public required string NombreNorm { get; set; }

    public required string DepartamentoCodigo { get; set; }

    public required string DepartamentoNombre { get; set; }
}
