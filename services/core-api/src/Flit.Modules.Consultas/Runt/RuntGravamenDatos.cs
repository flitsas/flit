using System.Text.Json;

namespace Flit.Modules.Consultas.Runt;

/// <summary>
/// Lectura pura de lo que el RUNT dice de gravámenes y prendas (Bug #13203), sin entidades de Trámites: la usan los
/// mappers de consulta para hidratar los campos y el semáforo, y <c>RuntGravamenSignal</c> (Trámites) para el gate de
/// prenda sobre los <c>field_values</c> de la instancia. Una sola regla para pantalla y gate (HU #13342 la separó).
/// </summary>
public static class RuntGravamenDatos
{
    public const string PrendasKey = "runt_tiene_prendas";
    public const string GravamenesKey = "runt_tiene_gravamenes";
    public const string DetalleKey = "runt_gravamenes";

    /// <summary>
    /// Formas afirmativas de las banderas, ya en mayúscula. Enumerables (Bug #13445) porque el filtro
    /// «Con prenda» del listado repite esta comparación en SQL: <see cref="EsAfirmativo"/> se deriva de
    /// esta lista para que el ícono y el <c>WHERE</c> no puedan separarse.
    /// </summary>
    public static readonly IReadOnlyList<string> ValoresAfirmativos = ["SI", "SÍ", "S", "TRUE", "1"];

    /// <summary>Formas NEGATIVAS explícitas de las banderas (Bug #13445): «el RUNT dijo que no».</summary>
    public static readonly IReadOnlyList<string> ValoresNegativos = ["NO", "N", "FALSE", "0"];

    /// <summary>El RUNT contesta «SI»/«NO» en texto; se acepta cualquier variante afirmativa razonable.</summary>
    public static bool EsAfirmativo(string? valor) =>
        valor is not null && ValoresAfirmativos.Contains(valor.Trim().ToUpperInvariant());

    /// <summary>¿La bandera dice explícitamente que NO? Vacío o ilegible no es «no»: es «no se sabe».</summary>
    public static bool EsNegativo(string? valor) =>
        valor is not null && ValoresNegativos.Contains(valor.Trim().ToUpperInvariant());

    // Mismos campos con los que el normalizador de consultas decide que un ítem tiene datos
    // (RuntGarantiasMobiliarias.Normalize): nombre, documento, idPrenda o fecha, en cualquiera de sus alias.
    private static readonly string[] CamposConDato =
    [
        "acreedor", "nombreAcreedor", "entidad",
        "numeroDocumentoAcreedor", "numeroDocumentoEntidad",
        "idPrenda",
        "fechaInscripcion", "fechaRegistro",
    ];

    /// <summary>
    /// Garantías con datos en el array JSON: cuenta los objetos que traen nombre, documento, idPrenda o
    /// fecha con valor (revisión PR #504, O3), igual que el normalizador. null, escalares, <c>{}</c> y
    /// objetos sin esos valores no cuentan. 0 si el JSON es null, vacío, no es array o no parsea.
    /// </summary>
    public static int ContarGarantias(string? detalleJson) => Garantias(detalleJson).Count;

    /// <summary>
    /// Bug #13445 — las garantías con datos del array (mismo criterio que <see cref="ContarGarantias"/>),
    /// con el acreedor de cada una: nombre (<c>acreedor</c>/<c>nombreAcreedor</c>/<c>entidad</c>) y
    /// documento (<c>numeroDocumentoAcreedor</c>/<c>numeroDocumentoEntidad</c>), el primero con valor.
    /// Vacía si el JSON es null, vacío, no es array o no parsea. El acreedor es PII: no loguear.
    /// </summary>
    public static IReadOnlyList<(string? AcreedorNombre, string? AcreedorDocumento)> Garantias(string? detalleJson)
    {
        if (string.IsNullOrWhiteSpace(detalleJson))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(detalleJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            return [.. doc.RootElement.EnumerateArray()
                .Where(TieneDato)
                .Select(item => (
                    Primero(item, "acreedor", "nombreAcreedor", "entidad"),
                    Primero(item, "numeroDocumentoAcreedor", "numeroDocumentoEntidad")))];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? Primero(JsonElement item, params string[] campos)
    {
        foreach (var campo in campos)
        {
            foreach (var p in item.EnumerateObject())
            {
                if (!string.Equals(p.Name, campo, StringComparison.OrdinalIgnoreCase))
                    continue;

                var valor = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Number => p.Value.GetRawText(),
                    _ => null,
                };
                if (!string.IsNullOrWhiteSpace(valor))
                    return valor.Trim();
            }
        }

        return null;
    }

    private static bool TieneDato(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object
        && item.EnumerateObject().Any(p =>
            CamposConDato.Contains(p.Name, StringComparer.OrdinalIgnoreCase)
            && p.Value.ValueKind switch
            {
                JsonValueKind.String => !string.IsNullOrWhiteSpace(p.Value.GetString()),
                JsonValueKind.Number => true,
                _ => false,
            });

    public static bool Reporta(string? prendas, string? gravamenes, string? detalleJson) =>
        EsAfirmativo(prendas) || EsAfirmativo(gravamenes) || ContarGarantias(detalleJson) > 0;
}
