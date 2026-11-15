namespace Flit.Diagnostico.Api.Modulos.Estadisticas;

/// <summary>Granularidad de la serie de E1–E3 (contrato-api §1): <c>DIA</c>, <c>SEMANA</c> (desde el lunes) o <c>MES</c>.</summary>
public enum Granularidad
{
    Dia,
    Semana,
    Mes,
}

/// <summary>
/// Cubetas de la serie (HU #14579, BE-1): la granularidad sale de los días del rango con los dos extremos incluidos (≤ 62 → DIA,
/// ≤ 183 → SEMANA, si no MES). La cubeta de un día es ese día, el lunes de su semana o el 1 de su mes; la serie trae todas las
/// cubetas que tocan el rango, en orden, para que las vacías salgan en cero.
/// </summary>
internal static class Cubetas
{
    public const int DiasMaximosPorDia = 62;
    public const int DiasMaximosPorSemana = 183;

    public static Granularidad De(int dias) => dias switch
    {
        <= DiasMaximosPorDia => Granularidad.Dia,
        <= DiasMaximosPorSemana => Granularidad.Semana,
        _ => Granularidad.Mes,
    };

    public static DateOnly Inicio(Granularidad granularidad, DateOnly dia) => granularidad switch
    {
        Granularidad.Dia => dia,
        Granularidad.Semana => dia.AddDays(-(((int)dia.DayOfWeek + 6) % 7)),
        _ => new DateOnly(dia.Year, dia.Month, 1),
    };

    public static IReadOnlyList<DateOnly> Del(Granularidad granularidad, DateOnly desde, DateOnly hasta)
    {
        var cubetas = new List<DateOnly>();
        for (var inicio = Inicio(granularidad, desde); inicio <= hasta; inicio = Siguiente(granularidad, inicio))
            cubetas.Add(inicio);
        return cubetas;
    }

    private static DateOnly Siguiente(Granularidad granularidad, DateOnly inicio) => granularidad switch
    {
        Granularidad.Dia => inicio.AddDays(1),
        Granularidad.Semana => inicio.AddDays(7),
        _ => inicio.AddMonths(1),
    };
}
