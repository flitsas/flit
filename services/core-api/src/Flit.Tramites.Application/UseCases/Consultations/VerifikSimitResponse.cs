using System.Text.Json.Serialization;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// DTOs para la respuesta SIMIT de Verifik (§3.5 por documento / §3.6 por placa).
/// Ambos endpoints comparten estructura. El envoltorio real es value.value.data
/// (doble anidamiento — quirk documentado del proveedor).
/// Todos los campos numéricos de multas vienen como number (no string).
/// </summary>
public sealed class VerifikSimitResponse
{
    [JsonPropertyName("value")]
    public VerifikSimitValueOuter? Value { get; set; }
}

public sealed class VerifikSimitValueOuter
{
    [JsonPropertyName("value")]
    public VerifikSimitValueInner? Value { get; set; }
}

public sealed class VerifikSimitValueInner
{
    [JsonPropertyName("data")]
    public VerifikSimitData? Data { get; set; }
}

public sealed class VerifikSimitData
{
    [JsonPropertyName("multas")]
    public List<VerifikSimitMulta>? Multas { get; set; }

    [JsonPropertyName("cursos")]
    public List<VerifikSimitCurso>? Cursos { get; set; }

    [JsonPropertyName("acuerdosPago")]
    public List<VerifikSimitAcuerdoPago>? AcuerdosPago { get; set; }

    [JsonPropertyName("totalMultasPagar")]
    public decimal? TotalMultasPagar { get; set; }

    [JsonPropertyName("cantMultasPagar")]
    public int? CantMultasPagar { get; set; }

    [JsonPropertyName("totalAcuerdosPagar")]
    public decimal? TotalAcuerdosPagar { get; set; }

    [JsonPropertyName("cantAcuerdosPagar")]
    public int? CantAcuerdosPagar { get; set; }
}

public sealed class VerifikSimitMulta
{
    [JsonPropertyName("infractor")]
    public VerifikSimitInfractor? Infractor { get; set; }

    [JsonPropertyName("valor")]
    public decimal? Valor { get; set; }

    [JsonPropertyName("placa")]
    public string? Placa { get; set; }

    [JsonPropertyName("infracciones")]
    public List<VerifikSimitInfraccion>? Infracciones { get; set; }

    [JsonPropertyName("valorPagar")]
    public decimal? ValorPagar { get; set; }

    [JsonPropertyName("departamento")]
    public string? Departamento { get; set; }

    [JsonPropertyName("organismoTransito")]
    public string? OrganismoTransito { get; set; }

    [JsonPropertyName("fechaComparendo")]
    public string? FechaComparendo { get; set; }

    [JsonPropertyName("fechaResolucion")]
    public string? FechaResolucion { get; set; }

    [JsonPropertyName("numeroComparendo")]
    public string? NumeroComparendo { get; set; }

    [JsonPropertyName("estadoComparendo")]
    public string? EstadoComparendo { get; set; }
}

public sealed class VerifikSimitInfractor
{
    [JsonPropertyName("tipoDocumento")]
    public string? TipoDocumento { get; set; }

    [JsonPropertyName("numeroDocumento")]
    public string? NumeroDocumento { get; set; }

    [JsonPropertyName("nombre")]
    public string? Nombre { get; set; }

    [JsonPropertyName("apellido")]
    public string? Apellido { get; set; }

    [JsonPropertyName("idTipoDocumento")]
    public int? IdTipoDocumento { get; set; }
}

public sealed class VerifikSimitInfraccion
{
    [JsonPropertyName("codigoInfraccion")]
    public string? CodigoInfraccion { get; set; }

    [JsonPropertyName("descripcionInfraccion")]
    public string? DescripcionInfraccion { get; set; }

    [JsonPropertyName("valorInfraccion")]
    public decimal? ValorInfraccion { get; set; }
}

public sealed class VerifikSimitCurso
{
    [JsonPropertyName("numeroMulta")]
    public string? NumeroMulta { get; set; }

    [JsonPropertyName("fechaCurso")]
    public string? FechaCurso { get; set; }

    [JsonPropertyName("numeroCurso")]
    public string? NumeroCurso { get; set; }

    [JsonPropertyName("ciudadRealizacion")]
    public string? CiudadRealizacion { get; set; }

    [JsonPropertyName("centroInstruccion")]
    public string? CentroInstruccion { get; set; }

    [JsonPropertyName("estado")]
    public string? Estado { get; set; }

    [JsonPropertyName("certificado")]
    public string? Certificado { get; set; }
}

public sealed class VerifikSimitAcuerdoPago
{
    [JsonPropertyName("resolucion")]
    public string? Resolucion { get; set; }

    [JsonPropertyName("fechaResolucion")]
    public string? FechaResolucion { get; set; }

    [JsonPropertyName("estado")]
    public string? Estado { get; set; }

    [JsonPropertyName("valorAcuerdo")]
    public decimal? ValorAcuerdo { get; set; }

    [JsonPropertyName("pendiente")]
    public decimal? Pendiente { get; set; }

    [JsonPropertyName("totalPagar")]
    public decimal? TotalPagar { get; set; }

    [JsonPropertyName("secretaria")]
    public string? Secretaria { get; set; }

    [JsonPropertyName("departamento")]
    public string? Departamento { get; set; }
}
