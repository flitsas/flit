using Flit.Infrastructure.Analytics.Scheduling;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Días de negocio en Bogotá.
///
/// <para>Los rangos que elige el usuario son días de calendario colombiano, no días UTC. La
/// diferencia no es teórica: con UTC, «hoy» empieza a las 7 p. m. del día anterior, y un informe
/// pedido a las ocho de la noche traería trámites del día siguiente y perdería los de la mañana.</para>
///
/// <para>Vive aparte porque lo usan los dos módulos de consultas —el del organismo y el de la
/// empresa gestora—, y dos definiciones de «hoy» harían que dos informes del mismo producto
/// contaran periodos distintos con la misma etiqueta.</para>
/// </summary>
internal static class BogotaDays
{
    public static TimeZoneInfo Zone => ScheduleDueEvaluator.BogotaTimeZone;

    /// <summary>Hoy en Bogotá.</summary>
    public static DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone).DateTime);

    /// <summary>
    /// Convierte un rango de días de negocio (Bogotá) al intervalo UTC correspondiente. El día
    /// «hasta» se incluye entero.
    ///
    /// <para>El resultado se normaliza a UTC: son el mismo instante, pero Npgsql rechaza un
    /// <c>DateTimeOffset</c> con offset distinto de cero contra una columna <c>timestamptz</c>, y
    /// estos valores viajan como parámetros. Sin esto la consulta revienta contra PostgreSQL aunque
    /// pase sobre InMemory.</para>
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To) Range(DateOnly from, DateOnly to)
    {
        var offset = Zone.GetUtcOffset(DateTime.SpecifyKind(
            from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified));

        return (
            new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), offset).ToUniversalTime(),
            new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), offset).ToUniversalTime());
    }
}
