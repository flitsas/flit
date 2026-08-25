using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flit.Modules.Quipux.Domain.Mapeo;

/// <summary>
/// Parametrización Quipux de un tipo de trámite: el bloque <c>quipux</c> de
/// <c>tramites.procedure_types.external_refs</c>.
/// </summary>
/// <remarks>
/// <para>
/// Es la pieza que convierte "añadir un tipo de trámite a Quipux" en un UPDATE, sin desplegar. En
/// FLIT 1.0 los códigos 16/213/13 y los prefijos TR/TRU/MI/MIL estaban como literales repartidos
/// entre servicios distintos (traspasos y matrículas eran ramas de código separadas que hacían lo
/// mismo), así que cada trámite nuevo era código nuevo.
/// </para>
/// <para>
/// La AUSENCIA del bloque <c>quipux</c> es el gate natural de elegibilidad: <see cref="Parse"/>
/// devuelve null y ese tipo de trámite simplemente no se radica. No hace falta una lista blanca
/// aparte que se desincronice.
/// </para>
/// <para>
/// Forma esperada:
/// <code>
/// {"quipux": {
///   "tipoTramite": 16, "tipoRequisito": 51, "prefijo": "TR",
///   "campoPlaca": "plate", "campoVin": null, "maxLongitudEmpresa": 35,
///   "variante": {"campo": "es_unilateral", "cuandoVerdadero": {"tipoTramite": 213, "prefijo": "TRU"}}
/// }}
/// </code>
/// </para>
/// </remarks>
public sealed class QuipuxTipoTramiteMap
{
    /// <summary>
    /// Familia Quipux del trámite: designa CUÁL de las tres banderas de la secretaría corresponde
    /// (<c>catalogs.transit_offices.quipux_registration</c> / <c>_transfer</c> / <c>_other</c>).
    /// Obligatorio. Ver <see cref="QuipuxFamilia"/>.
    /// </summary>
    /// <remarks>
    /// Va aquí, en <c>external_refs</c>, y NO se deriva de <c>procedure_types.family</c>: es la
    /// taxonomía de la secretaría, no la de FLIT, y un mismo trámite puede clasificarse distinto en
    /// cada una. ADR-0050 saneó <c>family</c> (CHECK de tres valores, sin <c>VEHICULAR</c>), así que
    /// ya no es la fiabilidad del dato lo que sostiene la separación; el DDL 83 verifica que ambas
    /// no se contradigan donde sí deben corresponderse.
    /// <para>NOTA: hoy ninguna de las tres banderas se consulta en el camino de radicación —
    /// <c>EncolarEnvioQuipuxHandler</c> no las lee—, así que esta familia se transporta pero no
    /// llega a decidir nada. Encender ese gate bloquearía radicaciones que hoy funcionan (las tres
    /// columnas tienen DEFAULT false), por lo que es una decisión de negocio pendiente.</para>
    /// </remarks>
    [JsonPropertyName("familia")]
    public string Familia { get; init; } = string.Empty;

    /// <summary>Código de trámite Quipux (16 traspaso bilateral, 13 matrícula). Obligatorio.</summary>
    [JsonPropertyName("tipoTramite")]
    public int TipoTramite { get; init; }

    /// <summary>Código de requisito Quipux (51 en todos los flujos conocidos). Obligatorio.</summary>
    [JsonPropertyName("tipoRequisito")]
    public int TipoRequisito { get; init; }

    /// <summary>Prefijo del nombre del documento (TR, MI). Obligatorio.</summary>
    [JsonPropertyName("prefijo")]
    public string Prefijo { get; init; } = string.Empty;

    /// <summary>
    /// Nombre del field value que trae la placa (<c>plate</c>), o null si el trámite no la usa
    /// (matrícula inicial: el vehículo aún no tiene placa).
    /// </summary>
    [JsonPropertyName("campoPlaca")]
    public string? CampoPlaca { get; init; }

    /// <summary>
    /// Nombre del field value que trae el VIN (<c>vin</c>), o null si el trámite no lo usa
    /// (traspaso: el identificador es la placa).
    /// </summary>
    [JsonPropertyName("campoVin")]
    public string? CampoVin { get; init; }

    /// <summary>Tope de la parte de empresa del nombre del documento: 35 traspaso, 25 matrícula. Obligatorio.</summary>
    [JsonPropertyName("maxLongitudEmpresa")]
    public int MaxLongitudEmpresa { get; init; }

    /// <summary>Variante opcional del trámite, condicionada a un field value. Ver <see cref="QuipuxVariante"/>.</summary>
    [JsonPropertyName("variante")]
    public QuipuxVariante? Variante { get; init; }

    /// <summary>
    /// Lee el bloque <c>quipux</c> de un <c>external_refs</c>.
    /// </summary>
    /// <param name="externalRefsJson">El jsonb completo de <c>procedure_types.external_refs</c>.</param>
    /// <returns>
    /// El mapa, o <c>null</c> si el tipo de trámite NO es elegible para Quipux: sin JSON, JSON
    /// inválido, sin clave <c>quipux</c>, o con un bloque a medio llenar. Un bloque incompleto se
    /// trata como no elegible en vez de radicar con <c>tipoTramite = 0</c>: un trámite mal radicado
    /// en una secretaría cuesta mucho más que uno no radicado, que además queda visible como
    /// pendiente.
    /// </returns>
    public static QuipuxTipoTramiteMap? Parse(string? externalRefsJson)
    {
        if (string.IsNullOrWhiteSpace(externalRefsJson))
        {
            return null;
        }

        QuipuxTipoTramiteMap? mapa;

        try
        {
            using var documento = JsonDocument.Parse(externalRefsJson);

            if (documento.RootElement.ValueKind != JsonValueKind.Object
                || !documento.RootElement.TryGetProperty("quipux", out var bloque)
                || bloque.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            mapa = bloque.Deserialize(QuipuxMapeoJsonContext.Default.QuipuxTipoTramiteMap);
        }
        catch (JsonException)
        {
            return null;
        }

        return mapa is not null && mapa.EsUtilizable() ? mapa : null;
    }

    /// <summary>
    /// Resuelve los códigos efectivos aplicando la variante si corresponde.
    /// </summary>
    /// <param name="fieldValues">
    /// Field values del trámite (<c>procedure_instance_field_values</c>), indexados por nombre de
    /// campo. Los valores son strings: en esa tabla todo se guarda como texto.
    /// </param>
    public QuipuxTramiteResuelto Resolve(IReadOnlyDictionary<string, string?> fieldValues)
    {
        ArgumentNullException.ThrowIfNull(fieldValues);

        var variante = Variante;
        var aplica = variante is not null
            && !string.IsNullOrWhiteSpace(variante.Campo)
            && fieldValues.TryGetValue(variante.Campo, out var valor)
            && EsVerdadero(valor);

        var cambios = aplica ? variante!.CuandoVerdadero : null;

        return new QuipuxTramiteResuelto(
            // La familia NO es sobreescribible por la variante: un traspaso unilateral sigue siendo
            // un traspaso, y la bandera de la secretaría que manda es la misma.
            Familia: Familia,
            TipoTramite: cambios?.TipoTramite ?? TipoTramite,
            TipoRequisito: cambios?.TipoRequisito ?? TipoRequisito,
            Prefijo: string.IsNullOrWhiteSpace(cambios?.Prefijo) ? Prefijo : cambios.Prefijo,
            MaxLongitudEmpresa: cambios?.MaxLongitudEmpresa ?? MaxLongitudEmpresa,
            CampoPlaca: CampoPlaca,
            CampoVin: CampoVin,
            VarianteAplicada: aplica);
    }

    /// <summary>
    /// ¿El field value cuenta como verdadero? Vocabulario cerrado: <c>true</c>, <c>1</c>, <c>si</c>,
    /// <c>sí</c> (sin distinguir mayúsculas ni espacios). Es necesario porque
    /// <c>procedure_instance_field_values</c> guarda booleanos como texto y la escritura ha variado
    /// entre el portal y las cargas administrativas. Cualquier otra cosa es falso: ante un valor
    /// ambiguo se prefiere el trámite base, que es el caso mayoritario.
    /// </summary>
    public static bool EsVerdadero(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return false;
        }

        var limpio = valor.Trim();

        return limpio.Equals("true", StringComparison.OrdinalIgnoreCase)
            || limpio.Equals("1", StringComparison.Ordinal)
            || limpio.Equals("si", StringComparison.OrdinalIgnoreCase)
            || limpio.Equals("sí", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ¿El bloque trae lo mínimo para radicar? No valida que el trámite tenga identificador
    /// (<c>campoPlaca</c>/<c>campoVin</c>): eso depende de los datos de cada instancia y lo resuelve
    /// el worker con <see cref="QuipuxTramiteResuelto.UsaPlaca"/> / <see cref="QuipuxTramiteResuelto.UsaVin"/>.
    /// </summary>
    /// <remarks>
    /// Valida el bloque base Y el delta de la variante. Lo segundo no es simetría cosmética: como
    /// <see cref="QuipuxVarianteValores"/> usa <c>int?</c> y <see cref="Resolve"/> hace
    /// <c>delta ?? base</c>, un <c>0</c> explícito en el JSON NO es null y por tanto GANA sobre el
    /// base. Sin esta validación, un <c>"cuandoVerdadero": {"tipoTramite": 0}</c> pasaría el gate
    /// (el base es válido) y se radicaría con tipo 0 en la secretaría. Como el objetivo del diseño
    /// es que añadir un trámite sea un UPDATE de <c>external_refs</c> sin desplegar, este es
    /// precisamente el error que llega por UPDATE — mejor no elegible que mal radicado.
    /// </remarks>
    private bool EsUtilizable() =>
        QuipuxFamilia.EsValida(Familia)
        && TipoTramite > 0
        && TipoRequisito > 0
        && !string.IsNullOrWhiteSpace(Prefijo)
        && MaxLongitudEmpresa > 0
        && VarianteEsUtilizable();

    /// <summary>
    /// El delta de la variante solo puede REDEFINIR con valores válidos; lo ausente hereda. Un
    /// entero presente pero no positivo es parametrización rota, no herencia.
    /// </summary>
    private bool VarianteEsUtilizable()
    {
        var cambios = Variante?.CuandoVerdadero;
        if (cambios is null)
        {
            return true;
        }

        // Una variante sin campo activador nunca se aplica: es configuración muerta, y si alguien
        // la escribió es que esperaba que hiciera algo.
        if (string.IsNullOrWhiteSpace(Variante!.Campo))
        {
            return false;
        }

        return cambios.TipoTramite is not (<= 0)
            && cambios.TipoRequisito is not (<= 0)
            && cambios.MaxLongitudEmpresa is not (<= 0)
            && (cambios.Prefijo is null || !string.IsNullOrWhiteSpace(cambios.Prefijo));
    }
}

/// <summary>
/// Variante de un tipo de trámite condicionada a un field value booleano.
/// </summary>
/// <remarks>
/// Existe porque los dos casos reales son la misma parametrización con un retoque: el traspaso
/// unilateral (<c>es_unilateral</c>) cambia código Y prefijo (16 → 213, TR → TRU), mientras la
/// matrícula en leasing (<c>es_leasing</c>) cambia SOLO el prefijo (MI → MIL, el código sigue 13).
/// Modelarlo como "base + delta" evita duplicar el bloque entero y que las dos copias se
/// desincronicen al editar una sola.
/// </remarks>
public sealed class QuipuxVariante
{
    /// <summary>Nombre del field value que activa la variante (<c>es_unilateral</c>, <c>es_leasing</c>).</summary>
    [JsonPropertyName("campo")]
    public string Campo { get; init; } = string.Empty;

    /// <summary>Qué cambia cuando el campo es verdadero. Lo ausente hereda del bloque base.</summary>
    [JsonPropertyName("cuandoVerdadero")]
    public QuipuxVarianteValores? CuandoVerdadero { get; init; }
}

/// <summary>
/// Delta de la variante: solo los valores que cambian respecto al bloque base. Todo es opcional —
/// null significa "hereda", no "ponlo en null".
/// </summary>
/// <remarks>
/// <c>campoPlaca</c> y <c>campoVin</c> NO son sobreescribibles a propósito: como su valor válido en
/// el base ya es null ("este trámite no usa placa"), aquí no habría forma de distinguir "ausente,
/// hereda" de "null explícito, quítalo". Ninguna variante conocida cambia el identificador del
/// vehículo; si alguna lo necesitara, el camino es un tipo de trámite propio, no un delta ambiguo.
/// </remarks>
public sealed class QuipuxVarianteValores
{
    /// <summary>Código de trámite Quipux de la variante (213 en el traspaso unilateral).</summary>
    [JsonPropertyName("tipoTramite")]
    public int? TipoTramite { get; init; }

    /// <summary>Código de requisito de la variante.</summary>
    [JsonPropertyName("tipoRequisito")]
    public int? TipoRequisito { get; init; }

    /// <summary>Prefijo de la variante (TRU, MIL).</summary>
    [JsonPropertyName("prefijo")]
    public string? Prefijo { get; init; }

    /// <summary>Tope de la parte de empresa de la variante.</summary>
    [JsonPropertyName("maxLongitudEmpresa")]
    public int? MaxLongitudEmpresa { get; init; }
}

/// <summary>
/// Códigos efectivos de un trámite concreto, ya con la variante aplicada. Es lo que consume el
/// worker: a partir de aquí no se vuelve a mirar la parametrización ni los field values.
/// </summary>
/// <param name="CampoPlaca">Field value del que leer la placa; null si el trámite no la usa.</param>
/// <param name="CampoVin">Field value del que leer el VIN; null si el trámite no lo usa.</param>
/// <param name="VarianteAplicada">¿Se activó la variante? Solo para trazabilidad: explica por qué salió 213 y no 16.</param>
public sealed record QuipuxTramiteResuelto(
    string Familia,
    int TipoTramite,
    int TipoRequisito,
    string Prefijo,
    int MaxLongitudEmpresa,
    string? CampoPlaca,
    string? CampoVin,
    bool VarianteAplicada)
{
    /// <summary>¿El identificador del vehículo es la placa?</summary>
    public bool UsaPlaca => !string.IsNullOrWhiteSpace(CampoPlaca);

    /// <summary>¿El identificador del vehículo es el VIN? (matrícula inicial: aún no hay placa).</summary>
    public bool UsaVin => !string.IsNullOrWhiteSpace(CampoVin);
}

/// <summary>
/// Contexto de serialización source-generated del bloque <c>quipux</c>. Evita la reflexión en
/// tiempo de ejecución (AOT/trimming), igual que <c>SnapshotJsonContext</c> en Admin.Domain.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(QuipuxTipoTramiteMap))]
internal sealed partial class QuipuxMapeoJsonContext : JsonSerializerContext;
