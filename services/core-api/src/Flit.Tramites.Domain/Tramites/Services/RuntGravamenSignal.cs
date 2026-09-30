using System.Text.Json;
using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Señal ÚNICA de «el RUNT reporta gravamen/prenda sobre este vehículo» (Bug #13203). La leen el gate
/// de prenda (<see cref="ProcedureTypeLayers.ExigeDecisionDePrenda"/> vía el estado del asistente) y
/// el semáforo <c>gravamenes</c> de los mappers de consulta, para que pantalla y gate no discrepen.
///
/// <para>Hay gravamen si alguna bandera es afirmativa (<c>runt_tiene_prendas</c> /
/// <c>runt_tiene_gravamenes</c>) O si el detalle <c>runt_gravamenes</c> es un array JSON con al menos
/// un elemento. Lo segundo cubre el caso real del bug: el RUNT contesta «NO»/«NO» y aun así trae una
/// garantía mobiliaria registrada en el RNGM. Un dato ausente, vacío o ilegible NO inventa gravamen —
/// eso convertiría cada consulta fallida en un bloqueo—, pero tampoco se oculta cuando sí vino.</para>
/// </summary>
public static class RuntGravamenSignal
{
    public const string PrendasKey = "runt_tiene_prendas";
    public const string GravamenesKey = "runt_tiene_gravamenes";
    public const string DetalleKey = "runt_gravamenes";

    /// <summary>El RUNT contesta «SI»/«NO» en texto; se acepta cualquier variante afirmativa razonable.</summary>
    public static bool EsAfirmativo(string? valor) =>
        valor?.Trim().ToUpperInvariant() is "SI" or "SÍ" or "S" or "TRUE" or "1";

    /// <summary>Elementos del array JSON de garantías; 0 si es null, vacío, no es array o no parsea.</summary>
    public static int ContarGarantias(string? detalleJson)
    {
        if (string.IsNullOrWhiteSpace(detalleJson))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(detalleJson);
            return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    public static bool Reporta(string? prendas, string? gravamenes, string? detalleJson) =>
        EsAfirmativo(prendas) || EsAfirmativo(gravamenes) || ContarGarantias(detalleJson) > 0;

    /// <summary>
    /// Evalúa sobre los <c>field_values</c> de la instancia. El detalle se lee de <c>ValueJson</c> (así
    /// lo hidratan los mappers) y, si viene vacío, de <c>ValueText</c>.
    /// </summary>
    public static bool Reporta(IEnumerable<ProcedureInstanceFieldValue> fieldValues)
    {
        ArgumentNullException.ThrowIfNull(fieldValues);

        string? prendas = null, gravamenes = null, detalle = null;
        foreach (var f in fieldValues)
        {
            if (string.Equals(f.FieldKey, PrendasKey, StringComparison.OrdinalIgnoreCase))
                prendas = f.ValueText;
            else if (string.Equals(f.FieldKey, GravamenesKey, StringComparison.OrdinalIgnoreCase))
                gravamenes = f.ValueText;
            else if (string.Equals(f.FieldKey, DetalleKey, StringComparison.OrdinalIgnoreCase))
                detalle = string.IsNullOrWhiteSpace(f.ValueJson) ? f.ValueText : f.ValueJson;
        }

        return Reporta(prendas, gravamenes, detalle);
    }
}
