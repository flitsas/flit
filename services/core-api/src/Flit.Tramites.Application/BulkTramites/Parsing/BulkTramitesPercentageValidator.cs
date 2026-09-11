using System.Globalization;

namespace Flit.Tramites.Application.BulkTramites.Parsing;

/// <summary>
/// Replica —sobre la fila ya parseada del Excel, antes de que exista el trámite— las mismas
/// reglas de reparto de propiedad que <c>PutActorsHandler</c> aplica al guardar actores
/// (ADR-0053, AC3 de HU #12522): con un solo actor por lado el porcentaje se ignora; con 2 o más,
/// los porcentajes son obligatorios, deben sumar 100 entre los del mismo lado y ninguno puede
/// quedar en 0. Solo aplica a la plantilla de Traspaso — Matrícula y Otros no tienen múltiples
/// actores por lado.
/// </summary>
public static class BulkTramitesPercentageValidator
{
    public const string PorcentajesNoSuman100 = "porcentajes_no_suman_100";
    public const string PorcentajeEnCero = "porcentaje_en_cero";

    private static readonly string[] Lados = ["comprador", "vendedor"];

    public static string? Validate(IReadOnlyDictionary<string, string?> values)
    {
        foreach (var lado in Lados)
        {
            var error = ValidateLado(values, lado);
            if (error is not null)
            {
                return error;
            }
        }

        return null;
    }

    private static string? ValidateLado(IReadOnlyDictionary<string, string?> values, string lado)
    {
        var porcentajes = new List<decimal>();

        for (var i = 1; i <= 4; i++)
        {
            var numeroDocumento = ValueOrNull(values, $"{lado}_{i}_numero_documento");
            if (numeroDocumento is null)
            {
                continue;
            }

            var crudo = ValueOrNull(values, $"{lado}_{i}_porcentaje");
            var porcentaje = crudo is not null
                && decimal.TryParse(crudo, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor)
                ? valor
                : 0m;

            porcentajes.Add(porcentaje);
        }

        // Un solo actor por lado (o ninguno): el porcentaje —si vino— se ignora, no bloquea.
        if (porcentajes.Count <= 1)
        {
            return null;
        }

        if (porcentajes.Any(p => p == 0m))
        {
            return PorcentajeEnCero;
        }

        return porcentajes.Sum() == 100m ? null : PorcentajesNoSuman100;
    }

    private static string? ValueOrNull(IReadOnlyDictionary<string, string?> values, string key) =>
        values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
}
