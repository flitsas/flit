using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Ítem de garantía/prenda del RUNT tal como lo mandan Kyverum (<c>data.garantias</c>,
/// <c>data.garantiasPrendas</c>) y Verifik (<c>data.garantiasMobiliarias</c>). Bug #13203: el RUNT
/// usa dos vocabularios para el mismo dato —el de prendas (<c>acreedor</c>,
/// <c>numeroDocumentoAcreedor</c>, <c>fechaInscripcion</c>, <c>estadoPrenda</c>) y el de garantías
/// mobiliarias del RNGM (<c>entidad</c>, <c>numeroDocumentoEntidad</c>, <c>fechaRegistro</c>,
/// <c>estado</c>)— y el modelo solo declaraba el primero: el acreedor se perdía en silencio. Cada
/// alias es una propiedad separada; <see cref="RuntGarantiasMobiliarias.Normalize"/> las une.
/// Los ids llegan como string o como número según la fuente: <see cref="RuntFlexibleIdConverter"/>
/// los acepta sin depender de las opciones del cliente HTTP.
/// </summary>
public sealed class RuntGarantiaMobiliaria
{
    [JsonPropertyName("idPrenda")]
    [JsonConverter(typeof(RuntFlexibleIdConverter))]
    public string? IdPrenda { get; set; }

    [JsonPropertyName("idVehiculoPrenda")]
    [JsonConverter(typeof(RuntFlexibleIdConverter))]
    public string? IdVehiculoPrenda { get; set; }

    [JsonPropertyName("acreedor")]
    public string? Acreedor { get; set; }

    [JsonPropertyName("nombreAcreedor")]
    public string? NombreAcreedor { get; set; }

    [JsonPropertyName("entidad")]
    public string? Entidad { get; set; }

    [JsonPropertyName("numeroDocumentoAcreedor")]
    public string? NumeroDocumentoAcreedor { get; set; }

    [JsonPropertyName("numeroDocumentoEntidad")]
    public string? NumeroDocumentoEntidad { get; set; }

    [JsonPropertyName("tipoDocumentoAcreedor")]
    public string? TipoDocumentoAcreedor { get; set; }

    [JsonPropertyName("tipoDocumentoEntidad")]
    public string? TipoDocumentoEntidad { get; set; }

    [JsonPropertyName("fechaInscripcion")]
    public string? FechaInscripcion { get; set; }

    [JsonPropertyName("fechaRegistro")]
    public string? FechaRegistro { get; set; }

    [JsonPropertyName("estadoPrenda")]
    public string? EstadoPrenda { get; set; }

    [JsonPropertyName("estado")]
    public string? Estado { get; set; }
}

/// <summary>
/// Lee un identificador que puede venir como string (<c>"2693079"</c>) o como número
/// (<c>2693079</c>) y lo expone como string. Cualquier otro tipo de token se descarta (null) en vez de
/// romper la deserialización de toda la respuesta.
/// </summary>
public sealed class RuntFlexibleIdConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                return reader.HasValueSequence
                    ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
                    : Encoding.UTF8.GetString(reader.ValueSpan);
            case JsonTokenType.Null:
                return null;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}

/// <summary>
/// Normalizador y semáforo comunes de garantías RUNT para Kyverum y Verifik (Bug #13203). Salida:
/// el contrato de <c>runt_gravamenes</c> que ya parsea el asistente (<c>idPrenda</c>,
/// <c>tipoDocumentoAcreedor</c>, <c>numeroDocumentoAcreedor</c>, <c>nombreAcreedor</c>,
/// <c>fechaInscripcion</c>, <c>estadoPrenda</c> + <c>idVehiculoPrenda</c>), camelCase y sin nulls.
/// </summary>
public static class RuntGarantiasMobiliarias
{
    private const string CheckKey = "gravamenes";
    private const string CheckLabel = "Gravámenes y limitaciones";

    private static readonly JsonSerializerOptions GravamenJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Une las listas y normaliza cada ítem al contrato común; descarta ítems sin datos.</summary>
    public static IReadOnlyList<NormalizedRuntGravamen> Normalize(params IEnumerable<RuntGarantiaMobiliaria?>?[] listas)
    {
        var result = new List<NormalizedRuntGravamen>();
        foreach (var g in listas.SelectMany(l => l ?? []))
        {
            if (g is null)
                continue;

            var nombre = FirstNonEmpty(g.Acreedor, g.NombreAcreedor, g.Entidad);
            var documento = FirstNonEmpty(g.NumeroDocumentoAcreedor, g.NumeroDocumentoEntidad);
            var idPrenda = FirstNonEmpty(g.IdPrenda);
            var fecha = FirstNonEmpty(g.FechaInscripcion, g.FechaRegistro);
            if (nombre is null && documento is null && idPrenda is null && fecha is null)
                continue;

            result.Add(new NormalizedRuntGravamen(
                idPrenda,
                FirstNonEmpty(g.TipoDocumentoAcreedor, g.TipoDocumentoEntidad),
                documento,
                nombre,
                fecha,
                FirstNonEmpty(g.EstadoPrenda, g.Estado),
                FirstNonEmpty(g.IdVehiculoPrenda)));
        }

        return result;
    }

    /// <summary>
    /// Hidrata <c>runt_nombre_acreedor</c> (primer acreedor con nombre) y <c>runt_gravamenes</c> (JSON
    /// normalizado). Sin garantías no escribe nada.
    /// </summary>
    public static void AddHydratedFields(List<HydratedField> fields, IReadOnlyList<NormalizedRuntGravamen> garantias)
    {
        if (garantias.Count == 0)
            return;

        var primerAcreedor = garantias.Select(g => g.NombreAcreedor).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        if (primerAcreedor is not null)
            fields.Add(new HydratedField("runt_nombre_acreedor", primerAcreedor, null));

        fields.Add(new HydratedField(
            RuntGravamenSignal.DetalleKey,
            null,
            JsonSerializer.Serialize(garantias, GravamenJsonOptions)));
    }

    /// <summary>
    /// Semáforo <c>gravamenes</c>. Nunca bloquea: <c>warn</c> si alguna bandera es afirmativa o hay al
    /// menos una garantía; <c>unknown</c> solo si no hay banderas ni garantías; <c>ok</c> en el resto.
    /// </summary>
    public static ConsultationCheck BuildCheck(string provider, string? gravamenes, string? prendas, int garantias)
    {
        if (RuntGravamenSignal.EsAfirmativo(gravamenes) || RuntGravamenSignal.EsAfirmativo(prendas))
        {
            return new ConsultationCheck(
                CheckKey, CheckLabel, "warn", provider,
                $"El vehículo tiene gravámenes o prendas (gravámenes: {NormSiNo(gravamenes)} · prendas: {NormSiNo(prendas)})");
        }

        if (garantias > 0)
        {
            return new ConsultationCheck(
                CheckKey, CheckLabel, "warn", provider,
                $"El RUNT registra {garantias} garantía(s) mobiliaria(s) (gravámenes: {NormSiNo(gravamenes)} · prendas: {NormSiNo(prendas)})");
        }

        if (string.IsNullOrWhiteSpace(gravamenes) && string.IsNullOrWhiteSpace(prendas))
            return new ConsultationCheck(CheckKey, CheckLabel, "unknown", provider, "Sin información de gravámenes");

        return new ConsultationCheck(
            CheckKey, CheckLabel, "ok", provider,
            "Sin gravámenes ni prendas registradas en el RUNT");
    }

    private static string NormSiNo(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim().ToUpperInvariant();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}

/// <summary>Contrato normalizado de un ítem de <c>runt_gravamenes</c>.</summary>
public sealed record NormalizedRuntGravamen(
    string? IdPrenda,
    string? TipoDocumentoAcreedor,
    string? NumeroDocumentoAcreedor,
    string? NombreAcreedor,
    string? FechaInscripcion,
    string? EstadoPrenda,
    string? IdVehiculoPrenda);
