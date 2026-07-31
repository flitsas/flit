using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// HU #11133 — <b>snapshot congelado de la consulta al RUES, por NIT y por trámite.</b>
///
/// <para><b>Qué resuelve.</b> El certificado RUES se re-consultaba al proveedor en cada regeneración
/// del expediente, y esas consultas se cobran por llamada. La regla del negocio es que el certificado
/// da fe de <b>lo que se consultó al registrar el trámite</b>: se guarda una vez y de ahí en adelante
/// se lee de base de datos, pase lo que pase con la empresa en el RUES.</para>
///
/// <para><b>Por qué por NIT y no por actor.</b> El asistente consulta el RUES <i>mientras</i> se
/// diligencia el formulario de actores, antes de que exista la fila del actor, así que no hay a quién
/// colgarle el dato en ese instante. La llave natural disponible es el NIT consultado.</para>
///
/// <para><b>Por qué un único <c>field_value</c> y no uno por NIT.</b> Un trámite puede tener dos
/// personas jurídicas (comprador y vendedor). Con llaves sueltas por NIT crecerían sin control; con un
/// solo documento indexado por NIT el número de llaves no depende de los datos. De paso corrige el
/// defecto de las llaves <c>rues_*</c> de instancia, que son un juego único por trámite y solo pueden
/// representar a UNA de las dos compañías.</para>
///
/// <para><b>Congelado.</b> <c>field_values</c> solo admite escritura mientras el trámite está en
/// edición (lo impone un trigger de base de datos), así que el snapshot queda naturalmente inmutable
/// en cuanto el trámite avanza. Es exactamente el comportamiento que se busca.</para>
/// </summary>
public static class RuesSnapshots
{
    /// <summary>Llave del <c>field_value</c> que guarda el documento completo.</summary>
    public const string FieldKey = "rues_snapshots_json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Una consulta al RUES congelada: cuándo se hizo y qué devolvió.</summary>
    public sealed record Entrada(
        [property: JsonPropertyName("queriedAt")] DateTimeOffset QueriedAt,
        [property: JsonPropertyName("fields")] Dictionary<string, string?> Fields);

    /// <summary>
    /// Incorpora al documento el resultado de una consulta. Reemplaza la entrada del mismo NIT (una
    /// reconsulta con el trámite aún en edición es una corrección deliberada del operador) y conserva
    /// las de los demás. Devuelve <c>null</c> si no hay nada que guardar.
    /// </summary>
    public static string? Merge(
        string? documentoActual,
        string? nit,
        IReadOnlyList<HydratedField> fields,
        DateTimeOffset queriedAt)
    {
        var llave = Normalizar(nit);
        if (llave is null || fields is null || fields.Count == 0)
            return documentoActual;

        var doc = Deserializar(documentoActual);
        doc[llave] = new Entrada(
            queriedAt,
            fields
                .GroupBy(f => f.FieldKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().ValueText, StringComparer.OrdinalIgnoreCase));

        return JsonSerializer.Serialize(doc, JsonOptions);
    }

    /// <summary>
    /// Datos congelados del NIT indicado, o <c>null</c> si ese NIT no se consultó en este trámite.
    /// Nunca lanza: un documento corrupto se trata como ausencia de snapshot.
    /// </summary>
    public static IReadOnlyDictionary<string, string?>? Read(string? documento, string? nit)
    {
        var llave = Normalizar(nit);
        if (llave is null)
            return null;

        var doc = Deserializar(documento);
        return doc.TryGetValue(llave, out var entrada) && entrada.Fields.Count > 0
            ? entrada.Fields
            : null;
    }

    /// <summary>Fecha en que se consultó el NIT indicado, para trazabilidad.</summary>
    public static DateTimeOffset? QueriedAt(string? documento, string? nit)
    {
        var llave = Normalizar(nit);
        if (llave is null)
            return null;

        return Deserializar(documento).TryGetValue(llave, out var entrada) ? entrada.QueriedAt : null;
    }

    private static Dictionary<string, Entrada> Deserializar(string? documento)
    {
        if (string.IsNullOrWhiteSpace(documento))
            return new Dictionary<string, Entrada>(StringComparer.OrdinalIgnoreCase);

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, Entrada>>(documento, JsonOptions)
                is { } parsed
                ? new Dictionary<string, Entrada>(parsed, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, Entrada>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            // Documento ilegible: se trata como si no hubiera snapshot. Perder la optimización es
            // aceptable; tumbar la generación del expediente por un JSON corrupto no lo es.
            return new Dictionary<string, Entrada>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? Normalizar(string? nit) =>
        string.IsNullOrWhiteSpace(nit) ? null : nit.Trim();
}
