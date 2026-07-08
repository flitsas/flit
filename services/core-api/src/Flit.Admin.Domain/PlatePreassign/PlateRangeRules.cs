using System.Globalization;

namespace Flit.Admin.Domain.PlatePreassign;

/// <summary>
/// Reglas de dominio de un rango de placas de preasignación (Feature #10587): validación y
/// explosión a placas individuales. Una placa colombiana = 3 letras + 3 dígitos (ej. ABC123);
/// el rango fija el prefijo de letras y un intervalo numérico [from, to] (000–999).
/// </summary>
public static class PlateRangeRules
{
    public const int MinNumber = 0;
    public const int MaxNumber = 999;

    /// <summary>Máximo de placas por rango (evita explosiones accidentales gigantes).</summary>
    public const int MaxPlatesPerRange = 1000;

    /// <summary>Ventana de edición del rango tras su creación (Feature #10587, consola OT).</summary>
    public static readonly TimeSpan EditWindow = TimeSpan.FromMinutes(60);

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

    /// <summary>Explota el rango en las placas individuales (inclusive en ambos extremos).</summary>
    public static IEnumerable<string> Enumerate(string prefix, int from, int to)
    {
        for (var n = from; n <= to; n++)
        {
            yield return Format(prefix, n);
        }
    }
}
