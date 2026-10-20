using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Modules.Consultas.Runt;
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
/// Todas las propiedades pasan por <see cref="RuntTolerantStringConverter"/>: un número llega como
/// string y cualquier otro tipo queda en null, sin tumbar la deserialización de la consulta entera
/// (revisión PR #504, L1) y sin depender de las opciones del cliente HTTP.
/// </summary>
public sealed class RuntGarantiaMobiliaria
{
    [JsonPropertyName("idPrenda")]
    [JsonConverter(typeof(RuntTolerantStringConverter))]
    public string? IdPrenda { get; set; }

    [JsonPropertyName("idVehiculoPrenda")]
    [JsonConverter(typeof(RuntTolerantStringConverter))]
    public string? IdVehiculoPrenda { get; set; }

    [JsonPropertyName("acreedor")]
    [JsonConverter(typeof(RuntTolerantStringConverter))]
    public string? Acreedor { get; set; }

    [JsonPropertyName("nombreAcreedor")]
    [JsonConverter(typeof(RuntTolerantStringConverter))]
    public string? NombreAcreedor { get; set; }

    [JsonPropertyName("entidad")]
    [JsonConverter(typeof(RuntTolerantStringConverter))]
    public string? Entidad { get; set; }

    [JsonPropertyName("numeroDocumentoAcreedor")]
    [JsonConverter(typeof(RuntTolerantStringConverter))]
    public string? NumeroDocumentoAcreedor { get; set; }

    [JsonPropertyName("numeroDocumentoEntidad")]
    [JsonConverter(typeof(RuntTolerantStringConverter))]
    public string? NumeroDocumentoEntidad { get; set; }

    [JsonPropertyName("tipoDocumentoAcreedor")]
    [JsonConverter(typeof(RuntTolerantStringConverter))]
    public string? TipoDocumentoAcreedor { get; set; }

    [JsonPropertyName("tipoDocumentoEntidad")]
    [JsonConverter(typeof(RuntTolerantStringConverter))]
    public string? TipoDocumentoEntidad { get; set; }

    [JsonPropertyName("fechaInscripcion")]
    [JsonConverter(typeof(RuntTolerantStringConverter))]
    public string? FechaInscripcion { get; set; }

    [JsonPropertyName("fechaRegistro")]
    [JsonConverter(typeof(RuntTolerantStringConverter))]
    public string? FechaRegistro { get; set; }

    [JsonPropertyName("estadoPrenda")]
    [JsonConverter(typeof(RuntTolerantStringConverter))]
    public string? EstadoPrenda { get; set; }

    [JsonPropertyName("estado")]
    [JsonConverter(typeof(RuntTolerantStringConverter))]
    public string? Estado { get; set; }
}

/// <summary>
/// Lee un escalar del RUNT que puede venir como string (<c>"2693079"</c>) o como número
/// (<c>2693079</c>) y lo expone como string. Cualquier otro tipo de token (bool, objeto, array) se
/// descarta (null) en vez de romper la deserialización de toda la respuesta.
/// </summary>
public sealed class RuntTolerantStringConverter : JsonConverter<string?>
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
/// Lista de garantías tolerante (revisión PR #504, L1): los ítems que no son objeto se descartan y un
/// valor que no es array (objeto, escalar) deja la lista en null. El proveedor ya cambió una vez
/// array-vs-objeto en este mismo campo y eso tumbaba la consulta Verifik completa.
/// </summary>
public sealed class RuntGarantiaListConverter : JsonConverter<List<RuntGarantiaMobiliaria>?>
{
    public override List<RuntGarantiaMobiliaria>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return null;
        }

        var list = new List<RuntGarantiaMobiliaria>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return list;

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                if (JsonSerializer.Deserialize<RuntGarantiaMobiliaria>(ref reader, options) is { } item)
                    list.Add(item);
            }
            else
            {
                reader.Skip();
            }
        }

        throw new JsonException("Array de garantías sin cerrar.");
    }

    public override void Write(Utf8JsonWriter writer, List<RuntGarantiaMobiliaria>? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
            JsonSerializer.Serialize(writer, item, options);
        writer.WriteEndArray();
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

    // Mismos literales de estado que declaran los mappers (Ok/Warn/Unknown): contrato con el frontend.
    private const string Ok = "ok";
    private const string Warn = "warn";
    private const string Unknown = "unknown";

    /// <summary>Clave del primer acreedor con nombre.</summary>
    public const string NombreAcreedorKey = "runt_nombre_acreedor";

    /// <summary>Valor de <c>runt_gravamenes</c> cuando el proveedor respondió sin garantías.</summary>
    public const string SinGarantiasJson = "[]";

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
    /// Hidrata las CUATRO claves de la señal de gravamen, siempre que el proveedor respondió
    /// (revisión PR #504, B1): <c>runt_tiene_gravamenes</c>/<c>runt_tiene_prendas</c> con su valor o
    /// null, <c>runt_nombre_acreedor</c> con el primer acreedor o null, y <c>runt_gravamenes</c> con el
    /// JSON normalizado o <c>"[]"</c>. El upsert de la consulta solo inserta o actualiza: sin escribir la
    /// clave, el detalle de una consulta anterior sobrevivía y <see cref="RuntGravamenSignal"/> lo seguía
    /// contando aunque el RUNT ya no reportara nada.
    /// </summary>
    public static void AddSignalFields(
        List<HydratedField> fields,
        string? gravamenes,
        string? prendas,
        IReadOnlyList<NormalizedRuntGravamen> garantias)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(garantias);

        fields.Add(new HydratedField(RuntGravamenDatos.GravamenesKey, Blank(gravamenes), null));
        fields.Add(new HydratedField(RuntGravamenDatos.PrendasKey, Blank(prendas), null));

        var primerAcreedor = garantias.Select(g => g.NombreAcreedor).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        fields.Add(new HydratedField(NombreAcreedorKey, primerAcreedor, null));

        fields.Add(new HydratedField(
            RuntGravamenDatos.DetalleKey,
            null,
            garantias.Count == 0 ? SinGarantiasJson : JsonSerializer.Serialize(garantias, GravamenJsonOptions)));
    }

    /// <summary>
    /// Semáforo <c>gravamenes</c>. Nunca bloquea: <c>warn</c> si alguna bandera es afirmativa o hay al
    /// menos una garantía; <c>unknown</c> solo si no hay banderas ni garantías; <c>ok</c> en el resto.
    /// </summary>
    public static ConsultationCheck BuildCheck(string provider, string? gravamenes, string? prendas, int garantias)
    {
        if (RuntGravamenDatos.EsAfirmativo(gravamenes) || RuntGravamenDatos.EsAfirmativo(prendas))
        {
            return new ConsultationCheck(
                CheckKey, CheckLabel, Warn, provider,
                $"El vehículo tiene gravámenes o prendas (gravámenes: {NormSiNo(gravamenes)} · prendas: {NormSiNo(prendas)})");
        }

        if (garantias > 0)
        {
            return new ConsultationCheck(
                CheckKey, CheckLabel, Warn, provider,
                $"El RUNT registra {garantias} garantía(s) mobiliaria(s) (gravámenes: {NormSiNo(gravamenes)} · prendas: {NormSiNo(prendas)})");
        }

        if (string.IsNullOrWhiteSpace(gravamenes) && string.IsNullOrWhiteSpace(prendas))
            return new ConsultationCheck(CheckKey, CheckLabel, Unknown, provider, "Sin información de gravámenes");

        return new ConsultationCheck(
            CheckKey, CheckLabel, Ok, provider,
            "Sin gravámenes ni prendas registradas en el RUNT");
    }

    /// <summary>Texto recortado, o null si viene vacío.</summary>
    public static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormSiNo(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim().ToUpperInvariant();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}

/// <summary>Contrato normalizado de un ítem de <c>runt_gravamenes</c>.</summary>
/// <param name="IdPrenda">Id de la prenda/garantía en el RUNT.</param>
/// <param name="TipoDocumentoAcreedor">Tipo de documento del acreedor (NIT, CC…).</param>
/// <param name="NumeroDocumentoAcreedor">
/// Documento del acreedor. PII (@pii:medium, ADR-0055, igual que <c>acreedor_documento</c>): no loguear.
/// </param>
/// <param name="NombreAcreedor">Nombre o razón social del acreedor.</param>
/// <param name="FechaInscripcion">Fecha de inscripción/registro, tal como la envía el RUNT.</param>
/// <param name="EstadoPrenda">Estado de la prenda/garantía.</param>
/// <param name="IdVehiculoPrenda">Id del vínculo vehículo-prenda en el RUNT.</param>
public sealed record NormalizedRuntGravamen(
    string? IdPrenda,
    string? TipoDocumentoAcreedor,
    string? NumeroDocumentoAcreedor,
    string? NombreAcreedor,
    string? FechaInscripcion,
    string? EstadoPrenda,
    string? IdVehiculoPrenda);
