using System.Globalization;
using System.Text.RegularExpressions;

namespace Flit.Admin.Domain.PlatePreassign;

/// <summary>
/// Reglas de dominio de un rango de placas de preasignación (Feature #10587): validación y
/// explosión a placas individuales. Los rangos enumerables siguen siendo carro/público
/// (3 letras + 3 dígitos, ej. ABC123). La asignación fuera de rango acepta además moto,
/// remolque/semirremolque y maquinaria (mismo criterio que el frontend <c>PLATE_PATTERN</c>).
/// </summary>
public static partial class PlateRangeRules
{
    public const int MinNumber = 0;
    public const int MaxNumber = 999;

    /// <summary>Máximo de placas por rango (evita explosiones accidentales gigantes).</summary>
    public const int MaxPlatesPerRange = 1000;

    /// <summary>
    /// Prefijo reservado del rango ad-hoc contenedor para placas con formato no enumerable
    /// (moto, remolque, maquinaria). La placa real vive en <c>plate_range_details.plate</c>.
    /// </summary>
    public const string NonStandardFormatPrefix = "NSF";

    /// <summary>Bounds del contenedor ad-hoc para formatos no enumerables.</summary>
    public const int NonStandardFormatNumber = 0;

    /// <summary>Mensaje 422 cuando la placa no cumple el patrón unificado.</summary>
    public const string InvalidPlateFormatMessage =
        "La placa no cumple el formato esperado. Ej: ABC123 (carro), ABC12D (moto), R12345 (remolque), S12345 (semirremolque) o MC029554 (maquinaria).";

    /// <summary>Ventana de edición del rango tras su creación (Feature #10587, consola OT).</summary>
    public static readonly TimeSpan EditWindow = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Unión de formatos colombianos alineada a <c>frontend/lib/validation/fieldRules.ts</c>
    /// (<c>PLATE_PATTERN</c>): carro <c>AAA123</c>, moto <c>AAA12A</c>/<c>AAA12</c>,
    /// remolque/semirremolque <c>R|S</c> + 5–6 dígitos, maquinaria <c>AA123456</c>.
    /// </summary>
    [GeneratedRegex(
        @"^([A-Z]{3}[0-9]{3}|[A-Z]{3}[0-9]{2}[A-Z]?|[RS][0-9]{5,6}|[A-Z]{2}[0-9]{6})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlatePattern();

    /// <summary>¿El prefijo son exactamente 3 letras A–Z?</summary>
    public static bool IsValidPrefix(string? prefix) =>
        prefix is { Length: 3 } && prefix.All(c => c is >= 'A' and <= 'Z');

    /// <summary>Valida un rango; devuelve el motivo del rechazo o null si es válido.</summary>
    public static string? Validate(string? prefix, int from, int to)
    {
        if (!IsValidPrefix(prefix))
        {
            return "El prefijo debe ser 3 letras mayúsculas (A–Z).";
        }

        if (from < MinNumber || to > MaxNumber || from > to)
        {
            return $"El rango numérico debe estar entre {MinNumber:D3} y {MaxNumber:D3} y 'desde' ≤ 'hasta'.";
        }

        if (to - from + 1 > MaxPlatesPerRange)
        {
            return $"El rango no puede exceder {MaxPlatesPerRange} placas.";
        }

        return null;
    }

    /// <summary>Formatea una placa a partir del prefijo y el número (ej. "ABC", 7 → "ABC007").</summary>
    public static string Format(string prefix, int number) =>
        prefix + number.ToString("D3", CultureInfo.InvariantCulture);

    /// <summary>¿La placa es carro/público enumerable (3 letras A–Z + 3 dígitos, ej. ABC123)?</summary>
    public static bool IsCarPlate(string? plate) =>
        plate is { Length: 6 }
        && plate[..3].All(c => c is >= 'A' and <= 'Z')
        && plate[3..].All(char.IsDigit);

    /// <summary>
    /// ¿La placa tiene un formato colombiano aceptado en asignación OT (carro, moto,
    /// remolque/semirremolque o maquinaria)?
    /// </summary>
    public static bool IsValidPlate(string? plate) =>
        !string.IsNullOrEmpty(plate) && PlatePattern().IsMatch(plate);

    /// <summary>
    /// Descompone la placa en prefijo + número para el rango contenedor del inventario
    /// (HU #10800). Carro <c>ABC123</c> → (<c>ABC</c>, 123). Otros formatos válidos →
    /// contenedor reservado (<c>NSF</c>, 0). Devuelve null si el formato es inválido.
    /// </summary>
    public static (string Prefix, int Number)? ParsePlate(string? plate)
    {
        if (!IsValidPlate(plate))
        {
            return null;
        }

        if (IsCarPlate(plate))
        {
            return (plate![..3], int.Parse(plate[3..], CultureInfo.InvariantCulture));
        }

        return (NonStandardFormatPrefix, NonStandardFormatNumber);
    }

    /// <summary>Explota el rango en las placas individuales (inclusive en ambos extremos).</summary>
    public static IEnumerable<string> Enumerate(string prefix, int from, int to)
    {
        for (var n = from; n <= to; n++)
        {
            yield return Format(prefix, n);
        }
    }
}
