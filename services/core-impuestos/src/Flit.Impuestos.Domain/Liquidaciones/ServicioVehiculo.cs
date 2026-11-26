using System.Globalization;
using System.Text;

namespace Flit.Impuestos.Domain.Liquidaciones;

/// <summary>
/// Tipo de servicio del vehículo según el RUNT (<c>informacion_general.tipo_servicio</c>: Particular, Público u Oficial). Un vehículo
/// de servicio público no paga impuesto vehicular (decisión del usuario, 2026-11-26): su ítem queda <c>NO_APLICA</c> sin llamar al
/// proveedor.
/// </summary>
/// <example>
/// <code>
/// ServicioVehiculo.EsPublico(" Público "); // true
/// ServicioVehiculo.EsPublico("Particular"); // false
/// </code>
/// </example>
public static class ServicioVehiculo
{
    private const string Publico = "PUBLICO";

    /// <summary>Vocales con diacrítico (minúscula y mayúscula) y su base, para quitar tildes sin depender de ICU.</summary>
    private const string ConDiacritico = "áàäâÁÀÄÂéèëêÉÈËÊíìïîÍÌÏÎóòöôÓÒÖÔúùüûÚÙÜÛ";
    private const string SinDiacritico = "aaaaAAAAeeeeEEEEiiiiIIIIooooOOOOuuuuUUUU";

    /// <summary>
    /// True si el texto, sin espacios a los lados, sin diacríticos y en mayúsculas, es <c>PUBLICO</c>. Null o vacío: false. Primero
    /// descompone (<see cref="NormalizationForm.FormD"/>) y descarta las marcas (<see cref="UnicodeCategory.NonSpacingMark"/>); como
    /// el servicio corre con globalización invariante (<c>System.Globalization.Invariant</c>, donde <c>Normalize</c> no descompone),
    /// las vocales precompuestas que queden se cambian por su base.
    /// </summary>
    public static bool EsPublico(string? tipoServicio)
    {
        if (string.IsNullOrWhiteSpace(tipoServicio))
            return false;
        var sinTildes = new StringBuilder(tipoServicio.Length);
        foreach (var c in tipoServicio.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            var i = ConDiacritico.IndexOf(c, StringComparison.Ordinal);
            sinTildes.Append(i < 0 ? c : SinDiacritico[i]);
        }

        return string.Equals(sinTildes.ToString().ToUpperInvariant(), Publico, StringComparison.Ordinal);
    }
}
