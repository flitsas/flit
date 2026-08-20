using System.Globalization;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// A4/B4 (HU #10673, ADR-0029) — compone el texto automático de observaciones del FUR cuando el operador
/// declaró una transformación de color y/o combustible durante el trámite. El cambio se DERIVA del diff
/// entre el snapshot RUNT (<c>*_runt</c>) y el valor efectivo, pero SOLO se imprime el valor NUEVO
/// (efectivo): en el FUR los campos del vehículo conservan el dato original del RUNT, así que la
/// observación solo debe declarar a qué se transformó. Se anexa (append) a las observaciones manuales sin
/// borrarlas. Ejemplo: <c>Cambio de color: ROJO. Cambio de combustible: DIESEL.</c>
/// </summary>
public static class FurTransformationObservations
{
    /// <summary>
    /// Devuelve las observaciones a imprimir en el FUR: las manuales con el texto automático de las
    /// transformaciones anexado. Si no hay cambios, devuelve las manuales sin tocar (puede ser null/vacío).
    /// </summary>
    public static string? Compose(
        string? manualObservations,
        string? colorRunt,
        string? colorEfectivo,
        string? fuelRunt,
        string? fuelEfectivo,
        string? bodyTypeRunt = null,
        string? bodyTypeEfectivo = null)
    {
        var auto = ComposeAuto(colorRunt, colorEfectivo, fuelRunt, fuelEfectivo, bodyTypeRunt, bodyTypeEfectivo);
        if (string.IsNullOrEmpty(auto))
            return manualObservations;

        return string.IsNullOrWhiteSpace(manualObservations)
            ? auto
            : $"{manualObservations.Trim()} {auto}";
    }

    /// <summary>
    /// HU #11643 — SOLO el texto automático, sin las observaciones manuales delante.
    ///
    /// <para>El recuadro tiene sitio contado y el texto libre del gestor no tiene tope, así que
    /// componerlos juntos aquí obligaba a que el recorte cayera sobre lo automático (iba al final de
    /// la cola). Separarlos permite a <see cref="FurObservacionesComposer"/> darle prioridad a lo que
    /// tiene consecuencias legales y recortar lo demás.</para>
    /// </summary>
    public static string? ComposeAuto(
        string? colorRunt,
        string? colorEfectivo,
        string? fuelRunt,
        string? fuelEfectivo,
        string? bodyTypeRunt = null,
        string? bodyTypeEfectivo = null)
    {
        var segments = new List<string>(3);
        if (HasChanged(colorRunt, colorEfectivo))
            segments.Add($"Cambio de color: {Display(colorEfectivo)}.");
        if (HasChanged(fuelRunt, fuelEfectivo))
            segments.Add($"Cambio de combustible: {Display(fuelEfectivo)}.");
        if (HasChanged(bodyTypeRunt, bodyTypeEfectivo))
            segments.Add($"Cambio de carrocería: {Display(bodyTypeEfectivo)}.");

        return segments.Count == 0 ? null : string.Join(" ", segments);
    }

    /// <summary>
    /// Hay cambio declarado si el snapshot RUNT existe y el valor efectivo (no vacío) difiere de él
    /// (comparación normalizada: trim + case-insensitive). Sin snapshot o sin efectivo no se declara nada.
    /// </summary>
    private static bool HasChanged(string? runt, string? efectivo) =>
        !string.IsNullOrWhiteSpace(runt)
        && !string.IsNullOrWhiteSpace(efectivo)
        && !string.Equals(runt.Trim(), efectivo.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Trim().ToUpper(CultureInfo.InvariantCulture);
}
