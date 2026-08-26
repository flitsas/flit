using System.Globalization;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Compone el texto automático de observaciones del FUR con el tipo de servicio del vehículo y la
/// empresa vinculadora que lo respalda. Mismo mecanismo que
/// <see cref="FurTransformationObservations"/>: se anexa a las observaciones sin borrar lo que
/// escribió el gestor. Ejemplo:
/// <c>Servicio: PÚBLICO. Empresa vinculadora: TRANSPORTES SAS, NIT 900123456.</c>
///
/// <para><b>Solo cuando hay vinculadora.</b> El tipo de servicio ya tiene su propia casilla en el
/// FUR (las marcas <c>vehicle_service_type_*</c>), así que repetirlo en el recuadro de observaciones
/// solo aporta cuando viene acompañado del dato que NO cabe en una casilla: quién es la empresa que
/// vincula el vehículo. Sin razón social no se imprime nada — ni "Servicio: PARTICULAR." suelto, que
/// solo gastaría renglones del recuadro.</para>
/// </summary>
public static class FurServicioVinculadoraObservation
{
    /// <summary>
    /// Nombres legibles de los códigos de <see cref="VehicleServiceTypeCode"/> (que es la lista
    /// canónica). El código viaja en <c>field_values</c> sin su etiqueta —esa vive en el catálogo que
    /// consume el frontend—, así que el FUR la resuelve aquí. Un código desconocido se imprime tal
    /// cual en mayúsculas antes que perderse: el recuadro es informativo y el dato sigue siendo real.
    /// </summary>
    private static readonly Dictionary<string, string> Legibles = new(StringComparer.OrdinalIgnoreCase)
    {
        [VehicleServiceTypeCode.Particular] = "PARTICULAR",
        [VehicleServiceTypeCode.Publico] = "PÚBLICO",
        [VehicleServiceTypeCode.Diplomatico] = "DIPLOMÁTICO",
        [VehicleServiceTypeCode.Oficial] = "OFICIAL",
        [VehicleServiceTypeCode.Especial] = "ESPECIAL",
        [VehicleServiceTypeCode.Otros] = "OTROS",
    };

    /// <summary>
    /// Devuelve el bloque a anexar, o <c>null</c> si no hay empresa vinculadora que declarar.
    /// Sin NIT se imprime solo la razón social, sin comas ni separadores sueltos que delaten el
    /// campo vacío (mismo criterio que <see cref="FurPrendaObservation"/>).
    /// </summary>
    public static string? Compose(string? tipoServicioCode, string? razonSocial, string? nit)
    {
        var empresa = razonSocial?.Trim();
        if (string.IsNullOrEmpty(empresa))
            return null;

        var documento = nit?.Trim();
        if (!string.IsNullOrEmpty(documento))
            empresa = $"{empresa}, NIT {documento}";

        var servicio = Legible(tipoServicioCode);
        return servicio is null
            ? $"Empresa vinculadora: {empresa}."
            : $"Servicio: {servicio}. Empresa vinculadora: {empresa}.";
    }

    private static string? Legible(string? code)
    {
        var normalizado = code?.Trim();
        if (string.IsNullOrEmpty(normalizado))
            return null;

        return Legibles.TryGetValue(normalizado, out var legible)
            ? legible
            : normalizado.ToUpper(CultureInfo.InvariantCulture);
    }
}
