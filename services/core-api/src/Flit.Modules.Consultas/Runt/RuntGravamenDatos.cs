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

    /// <summary>El RUNT contesta «SI»/«NO» en texto; se acepta cualquier variante afirmativa razonable.</summary>
    public static bool EsAfirmativo(string? valor) =>
        valor?.Trim().ToUpperInvariant() is "SI" or "SÍ" or "S" or "TRUE" or "1";

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
    public static int ContarGarantias(string? detalleJson)
    {
        if (string.IsNullOrWhiteSpace(detalleJson))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(detalleJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return 0;

            return doc.RootElement.EnumerateArray().Count(TieneDato);
        }
        catch (JsonException)
        {
            return 0;
        }
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
