using System.Text.Json.Serialization;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// FEATURE 05 — respuesta de la fuente INTERNA de comparendos (API de registro de FLIT,
/// heredado de FLIT 1: <c>api/v1/registration/simit</c>).
///
/// Es el MISMO payload del SIMIT que Verifik entrega envuelto en <c>value.value.data</c>, pero
/// PLANO (sin envoltorio) y con todos los campos numéricos serializados como texto
/// (p. ej. <c>"valorPagar": "742730"</c>). Los <c>decimal?</c>/<c>int?</c> funcionan igual porque
/// el provider deserializa con <see cref="System.Text.Json.JsonSerializerDefaults.Web"/>, que
/// activa <c>AllowReadingFromString</c>. Verificado contra la respuesta real (HTTP 200).
///
/// ⚠️ Los campos AGREGADOS de esta respuesta NO son fiables y por eso no se mapean:
/// <c>totalMultasPagar</c> devuelve el NÚMERO de multas (p. ej. "26"), no el monto — el monto vive
/// en <c>totalMultas</c> — y <c>cantMultasPagar</c> devuelve "0" aun habiendo multas pendientes.
/// El conteo y el importe se calculan recorriendo <see cref="Multas"/>, igual que en el mapper de
/// Verifik. Ver <see cref="FlitFinesResultMapper"/>.
///
/// Solo se declaran los campos que el mapper consume: el resto del payload (cursos, resoluciones,
/// proyección, datos del infractor…) se ignora deliberadamente. El infractor trae PII y no se
/// mapea ni se registra en trazas (Habeas Data).
/// </summary>
public sealed class FlitFinesResponse
{
    [JsonPropertyName("multas")]
    public List<FlitFinesMulta>? Multas { get; set; }

    [JsonPropertyName("acuerdosPago")]
    public List<FlitFinesAcuerdoPago>? AcuerdosPago { get; set; }
}

public sealed class FlitFinesMulta
{
    /// <summary>"Pendiente" | "Pendiente Curso" | "Pagado" | null. Ver nota en el mapper.</summary>
    [JsonPropertyName("estadoComparendo")]
    public string? EstadoComparendo { get; set; }

    /// <summary>
    /// Estado de CARTERA (cobro). En la respuesta viva el comparendo escalado a resolución llega con
    /// <c>estadoComparendo=null</c> pero <c>estadoCartera="Pendiente de pago"</c>: indicador real de deuda.
    /// </summary>
    [JsonPropertyName("estadoCartera")]
    public string? EstadoCartera { get; set; }

    /// <summary>Llega como texto ("742730"); AllowReadingFromString lo convierte.</summary>
    [JsonPropertyName("valorPagar")]
    public decimal? ValorPagar { get; set; }

    [JsonPropertyName("numeroComparendo")]
    public string? NumeroComparendo { get; set; }

    [JsonPropertyName("fechaComparendo")]
    public string? FechaComparendo { get; set; }

    [JsonPropertyName("organismoTransito")]
    public string? OrganismoTransito { get; set; }

    [JsonPropertyName("infracciones")]
    public List<FlitFinesInfraccion>? Infracciones { get; set; }
}

public sealed class FlitFinesInfraccion
{
    [JsonPropertyName("codigoInfraccion")]
    public string? CodigoInfraccion { get; set; }

    [JsonPropertyName("descripcionInfraccion")]
    public string? DescripcionInfraccion { get; set; }
}

public sealed class FlitFinesAcuerdoPago
{
    [JsonPropertyName("estado")]
    public string? Estado { get; set; }

    [JsonPropertyName("pendiente")]
    public decimal? Pendiente { get; set; }
}
