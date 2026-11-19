using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Carga;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Vehiculos;

namespace Flit.Diagnostico.Api.Modulos.Vehiculos;

/// <summary>
/// Corrección de una fila del archivo (HU #13751, decisión «Reenviar el archivo»): el número de fila del análisis y, por
/// cada campo, si vino en la corrección y su valor. Un campo que no vino conserva el del archivo; uno que vino con
/// <c>null</c> o vacío lo deja vacío. Es una clase (no un record) para que ningún <c>ToString</c> automático lleve el
/// documento a un log.
/// </summary>
internal sealed class CorreccionFila
{
    public required int Fila { get; init; }

    public bool TraeIdentificador { get; init; }

    public string? Identificador { get; init; }

    public bool TraeTipoDocumento { get; init; }

    public string? TipoDocumentoLocatario { get; init; }

    public bool TraeDocumento { get; init; }

    public string? DocumentoLocatario { get; init; }
}

/// <summary>
/// Lote a confirmar a partir del MISMO archivo del análisis (HU #13751, F02-H5, R-3, decisión del usuario 2026-10-21
/// «Reenviar el archivo»): el análisis devuelve el documento del locatario enmascarado, así que la confirmación vuelve a
/// leer el archivo para tener el documento completo. Las filas salen en el orden del archivo, con las correcciones
/// aplicadas por número de fila; la revalidación, el descarte de inválidos y duplicados y el límite los hace
/// <see cref="ConfirmadorVehiculos"/>. Un archivo distinto del analizado no es un riesgo: todo se revalida. Ningún mensaje
/// de error lleva valores de las filas.
/// </summary>
/// <example>
/// <code>
/// var correcciones = LoteDesdeArchivo.LeerCorrecciones("""[{"fila": 2, "identificador": "ABC123"}]""");
/// var capturados = LoteDesdeArchivo.Capturados(FormatoCarga.Leer("lote.csv", bytes), correcciones);
/// </code>
/// </example>
internal static class LoteDesdeArchivo
{
    public const string CampoCorrecciones = "correcciones";
    public const string CodigoCorreccionesInvalidas = "CORRECCIONES_INVALIDAS";

    private const string PropiedadFila = "fila";
    private const string PropiedadIdentificador = "identificador";
    private const string PropiedadTipo = "tipo_documento_locatario";
    private const string PropiedadDocumento = "documento_locatario";

    /// <summary>
    /// Lee el JSON de <see cref="CampoCorrecciones"/>: un arreglo de objetos con <c>fila</c> (entero, sin repetir) y,
    /// opcionales, <c>identificador</c>, <c>tipo_documento_locatario</c> y <c>documento_locatario</c> (texto o null).
    /// Vacío o ausente es sin correcciones. Cualquier otra forma es 422 <see cref="CodigoCorreccionesInvalidas"/>.
    /// </summary>
    public static IReadOnlyDictionary<int, CorreccionFila> LeerCorrecciones(string? json)
    {
        var porFila = new Dictionary<int, CorreccionFila>();
        if (string.IsNullOrWhiteSpace(json))
            return porFila;

        JsonDocument documento;
        try
        {
            documento = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw Invalidas("Las correcciones no son un JSON válido.");
        }

        using (documento)
        {
            if (documento.RootElement.ValueKind != JsonValueKind.Array)
                throw Invalidas("Las correcciones deben ser una lista de filas.");
            foreach (var elemento in documento.RootElement.EnumerateArray())
            {
                if (elemento.ValueKind != JsonValueKind.Object
                    || !elemento.TryGetProperty(PropiedadFila, out var fila) || fila.ValueKind != JsonValueKind.Number || !fila.TryGetInt32(out var numero))
                {
                    throw Invalidas("Cada corrección debe indicar el número de fila del análisis en «fila».");
                }

                var (traeIdentificador, identificador) = Texto(elemento, PropiedadIdentificador);
                var (traeTipo, tipo) = Texto(elemento, PropiedadTipo);
                var (traeDocumento, numeroDocumento) = Texto(elemento, PropiedadDocumento);
                var correccion = new CorreccionFila
                {
                    Fila = numero,
                    TraeIdentificador = traeIdentificador,
                    Identificador = identificador,
                    TraeTipoDocumento = traeTipo,
                    TipoDocumentoLocatario = tipo,
                    TraeDocumento = traeDocumento,
                    DocumentoLocatario = numeroDocumento,
                };
                if (!porFila.TryAdd(numero, correccion))
                    throw Invalidas($"La fila {numero} viene corregida más de una vez.");
            }
        }

        return porFila;
    }

    /// <summary>
    /// Las filas del archivo como vehículos capturados, en su orden, con las correcciones aplicadas. Sin la columna del
    /// identificador es 422 COLUMNA_REQUERIDA; una corrección de una fila que el archivo no tiene es 422
    /// <see cref="CodigoCorreccionesInvalidas"/>.
    /// </summary>
    public static IReadOnlyList<VehiculoCapturado> Capturados(TablaCarga tabla, IReadOnlyDictionary<int, CorreccionFila> correcciones)
    {
        ArgumentNullException.ThrowIfNull(tabla);
        ArgumentNullException.ThrowIfNull(correcciones);
        var (columnaIdentificador, columnaTipo, columnaDocumento) = AnalizadorCarga.Columnas(tabla);
        var numeros = tabla.Filas.Select(f => f.Numero).ToHashSet();
        var ajena = correcciones.Keys.Where(n => !numeros.Contains(n)).Order().FirstOrDefault(-1);
        if (ajena != -1)
            throw Invalidas($"La fila {ajena} no está en el archivo: corrija solo filas del análisis de este archivo.");

        return [.. tabla.Filas.Select(leida =>
        {
            var identificador = AnalizadorCarga.Celda(leida, columnaIdentificador);
            var tipo = AnalizadorCarga.Celda(leida, columnaTipo);
            var documento = AnalizadorCarga.Celda(leida, columnaDocumento);
            if (correcciones.TryGetValue(leida.Numero, out var c))
            {
                identificador = c.TraeIdentificador ? c.Identificador : identificador;
                tipo = c.TraeTipoDocumento ? c.TipoDocumentoLocatario : tipo;
                documento = c.TraeDocumento ? c.DocumentoLocatario : documento;
            }

            return new VehiculoCapturado(identificador, tipo, documento);
        })];
    }

    private static (bool Trae, string? Valor) Texto(JsonElement elemento, string propiedad)
    {
        if (!elemento.TryGetProperty(propiedad, out var valor))
            return (false, null);
        return valor.ValueKind switch
        {
            JsonValueKind.Null => (true, null),
            JsonValueKind.String => (true, valor.GetString()),
            _ => throw Invalidas($"«{propiedad}» de una corrección debe ser texto o null."),
        };
    }

    private static ReglaException Invalidas(string mensaje) => new(CodigoCorreccionesInvalidas, mensaje) { Campo = CampoCorrecciones };
}
