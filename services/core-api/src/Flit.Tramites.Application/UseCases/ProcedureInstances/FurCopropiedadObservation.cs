using System.Globalization;
using Flit.Tramites.Application.Documents;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Bloque del párrafo 23 que declara a cada copropietario y su porcentaje.
/// Canónico: <c>docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md</c> (después del bloque de tipo, antes del gravamen).
/// </summary>
public static class FurCopropiedadObservation
{
    public static string? Compose(IReadOnlyList<DocumentParte> partes)
    {
        if (partes.Count == 0)
            return null;

        var vendedores = Lado(partes, "vendedor");
        var compradores = Lado(partes, "comprador");
        return FurPrendaObservation.Join(vendedores, compradores);
    }

    private static string? Lado(IReadOnlyList<DocumentParte> partes, string rol)
    {
        var grupo = partes
            .Where(p => string.Equals(p.Rol, rol, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Ordinal)
            .ToList();
        if (grupo.Count < 2)
            return null;

        return string.Join(
            " ",
            grupo.Select(p =>
            {
                var nombre = (p.Nombre ?? string.Empty).Trim().ToUpperInvariant();
                return $"{nombre} es el propietario del {FormatoPorcentaje(p.OwnershipPercentage)}%.";
            }));
    }

    public static string FormatoPorcentaje(decimal? value)
    {
        if (value is null)
            return "0";
        var n = decimal.Round(value.Value, 2, MidpointRounding.AwayFromZero);
        return n == decimal.Truncate(n)
            ? decimal.Truncate(n).ToString(CultureInfo.InvariantCulture)
            : n.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
