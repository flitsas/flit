namespace Flit.Analytics.Application.Queries.Metrics;

/// <summary>
/// Validación y cálculo de ventanas comunes a los handlers de métricas (Reportes 2.0 §4.1).
/// Errores (tupla <c>(result, error)</c> como los handlers existentes):
/// <c>invalid_range</c> · <c>invalid_compare_with</c> · <c>invalid_stuck_days</c>.
/// </summary>
public static class MetricsQueryValidation
{
    public const string PreviousPeriod = "previous_period";
    public const string PreviousYear = "previous_year";

    public const int DefaultStuckDays = 7;
    public const int MinStuckDays = 1;
    public const int MaxStuckDays = 90;

    /// <summary>¿<paramref name="compareWith"/> es válido? (null/vacío = sin comparación).</summary>
    public static bool IsValidCompareWith(string? compareWith) =>
        string.IsNullOrWhiteSpace(compareWith)
        || compareWith is PreviousPeriod or PreviousYear;

    /// <summary>¿<paramref name="stuckDays"/> está en el rango 1..90? (null = default 7).</summary>
    public static bool IsValidStuckDays(int? stuckDays) =>
        stuckDays is null or (>= MinStuckDays and <= MaxStuckDays);

    /// <summary>
    /// Ventana de comparación (§4.1): <c>previous_period</c> = misma duración inmediatamente
    /// anterior (prevTo = from−1 día; prevFrom = prevTo−(len−1)); <c>previous_year</c> = mismas
    /// fechas un año atrás. Null cuando no se pidió comparación.
    /// </summary>
    public static (DateOnly From, DateOnly To)? ResolveComparisonWindow(
        string? compareWith, DateOnly from, DateOnly to)
    {
        if (string.IsNullOrWhiteSpace(compareWith))
            return null;

        if (compareWith == PreviousYear)
            return (from.AddYears(-1), to.AddYears(-1));

        // previous_period
        var lengthDays = to.DayNumber - from.DayNumber + 1;
        var prevTo = from.AddDays(-1);
        var prevFrom = prevTo.AddDays(-(lengthDays - 1));
        return (prevFrom, prevTo);
    }
}
