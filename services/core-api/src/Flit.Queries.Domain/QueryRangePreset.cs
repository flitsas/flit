namespace Flit.Queries.Domain;

/// <summary>
/// Rangos con nombre. Se guardan en relativo para que la consulta siga viva; ver
/// <see cref="QueryDateFilter"/>.
///
/// <para>Compartido a propósito entre el organismo y la empresa: si «mes anterior» se resolviera
/// distinto en dos módulos, dos informes del mismo producto contarían periodos distintos con la
/// misma etiqueta y nadie sabría cuál creer.</para>
/// </summary>
public static class QueryRangePreset
{
    public const string Hoy = "hoy";
    public const string Ultimos7 = "ultimos_7";
    public const string Ultimos30 = "ultimos_30";
    public const string Ultimos90 = "ultimos_90";
    public const string MesActual = "mes_actual";
    public const string MesAnterior = "mes_anterior";
    public const string AnioActual = "anio_actual";
    public const string Personalizado = "personalizado";

    public static bool IsKnown(string? preset) => preset is
        Hoy or Ultimos7 or Ultimos30 or Ultimos90
        or MesActual or MesAnterior or AnioActual or Personalizado;

    public static IReadOnlyList<QueryFieldOptionDto> Options { get; } =
    [
        new(Hoy, "Hoy"),
        new(Ultimos7, "Últimos 7 días"),
        new(Ultimos30, "Últimos 30 días"),
        new(Ultimos90, "Últimos 90 días"),
        new(MesActual, "Mes actual"),
        new(MesAnterior, "Mes anterior"),
        new(AnioActual, "Año actual"),
        new(Personalizado, "Rango propio"),
    ];

    /// <summary>
    /// Resuelve el rango contra el día indicado (el de Bogotá, que lo pone el repositorio).
    /// Un preset desconocido cae a los últimos 30 días en vez de reventar: una consulta guardada
    /// con un preset que dejó de existir debe seguir abriendo.
    /// </summary>
    public static (DateOnly From, DateOnly To) Resolve(QueryDateFilter filter, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(filter);

        switch (filter.Preset)
        {
            case Hoy:
                return (today, today);
            case Ultimos7:
                return (today.AddDays(-6), today);
            case Ultimos90:
                return (today.AddDays(-89), today);
            case MesActual:
                return (new DateOnly(today.Year, today.Month, 1), today);
            case MesAnterior:
                var primeroDeEste = new DateOnly(today.Year, today.Month, 1);
                var primeroDelAnterior = primeroDeEste.AddMonths(-1);
                return (primeroDelAnterior, primeroDeEste.AddDays(-1));
            case AnioActual:
                return (new DateOnly(today.Year, 1, 1), today);
            case Personalizado:
                // Sin extremos guardados no hay nada que respetar: se cae al defecto en vez de
                // devolver un rango vacío que se leería como «no hay trámites».
                var from = filter.From ?? today.AddDays(-29);
                var to = filter.To ?? today;
                return from > to ? (to, from) : (from, to);
            default:
                return (today.AddDays(-29), today);
        }
    }
}
