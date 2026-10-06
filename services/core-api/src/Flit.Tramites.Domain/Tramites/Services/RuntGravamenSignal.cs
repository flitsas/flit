using Flit.Modules.Consultas.Runt;
using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Señal ÚNICA de «el RUNT reporta gravamen/prenda sobre este vehículo» (Bug #13203). La leen el gate
/// de prenda (<see cref="ProcedureTypeLayers.ExigeDecisionDePrenda"/> vía el estado del asistente) y
/// el semáforo <c>gravamenes</c> de los mappers de consulta, para que pantalla y gate no discrepen.
///
/// <para>Hay gravamen si alguna bandera es afirmativa (<c>runt_tiene_prendas</c> /
/// <c>runt_tiene_gravamenes</c>) O si el detalle <c>runt_gravamenes</c> es un array JSON con al menos
/// un elemento con datos. Lo segundo cubre el caso real del bug: el RUNT contesta «NO»/«NO» y aun así trae una
/// garantía mobiliaria registrada en el RNGM. Un dato ausente, vacío o ilegible NO inventa gravamen —
/// eso convertiría cada consulta fallida en un bloqueo—, pero tampoco se oculta cuando sí vino.</para>
/// </summary>
public static class RuntGravamenSignal
{
    // HU #13342 (Epic #13316): la lectura pura vive en el módulo de consultas (RuntGravamenDatos), que la usa para
    // hidratar; aquí queda la API de siempre y la evaluación sobre los field_values de la instancia.
    public const string PrendasKey = RuntGravamenDatos.PrendasKey;
    public const string GravamenesKey = RuntGravamenDatos.GravamenesKey;
    public const string DetalleKey = RuntGravamenDatos.DetalleKey;

    /// <summary>El RUNT contesta «SI»/«NO» en texto; se acepta cualquier variante afirmativa razonable.</summary>
    public static bool EsAfirmativo(string? valor) => RuntGravamenDatos.EsAfirmativo(valor);

    /// <summary>Garantías con datos en el array JSON (ver <see cref="RuntGravamenDatos.ContarGarantias"/>).</summary>
    public static int ContarGarantias(string? detalleJson) => RuntGravamenDatos.ContarGarantias(detalleJson);

    public static bool Reporta(string? prendas, string? gravamenes, string? detalleJson) =>
        RuntGravamenDatos.Reporta(prendas, gravamenes, detalleJson);

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
