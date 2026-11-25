using System.Globalization;
using Flit.Impuestos.Api.Plataforma;

namespace Flit.Impuestos.Api.Modulos.Comun;

/// <summary>
/// Rango de días de Colombia (UTC−5 fijo) del dashboard y los reportes (plan §5.1: <c>desde</c> y <c>hasta</c>, 30 días por defecto,
/// 400 si el rango no es válido). <see cref="Desde"/> y <see cref="Hasta"/> cuentan los dos: <see cref="Inicio"/> es las 00:00 de
/// <c>desde</c> y <see cref="Fin"/> las 00:00 del día siguiente a <c>hasta</c> (rango semiabierto).
/// </summary>
/// <example>
/// <code>
/// var rango = RangoDias.Leer(http.Request.Query, RangoDias.Hoy(reloj));
/// var filas = db.Set&lt;Liquidacion&gt;().Where(l => l.CreadaEn >= rango.Inicio &amp;&amp; l.CreadaEn &lt; rango.Fin);
/// </code>
/// </example>
internal readonly record struct RangoDias(DateOnly Desde, DateOnly Hasta)
{
    public const int DiasPorDefecto = 30;

    /// <summary>Rango máximo en días, con los dos extremos incluidos.</summary>
    public const int DiasMaximos = 366;

    public const string CodigoFiltroInvalido = "FILTRO_INVALIDO";
    public const string CodigoRangoInvalido = "RANGO_INVALIDO";

    public static readonly TimeSpan OffsetColombia = TimeSpan.FromHours(-5);

    public int Dias => Hasta.DayNumber - Desde.DayNumber + 1;

    public DateTimeOffset Inicio => InicioDelDia(Desde);

    public DateTimeOffset Fin => InicioDelDia(Hasta.AddDays(1));

    public static DateOnly DiaDe(DateTimeOffset instante) => DateOnly.FromDateTime(instante.ToOffset(OffsetColombia).DateTime);

    public static DateOnly Hoy(TimeProvider reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        return DiaDe(reloj.GetUtcNow());
    }

    /// <summary>
    /// Lee <c>desde</c> y <c>hasta</c> (AAAA-MM-DD). Sin ninguno: los últimos <see cref="DiasPorDefecto"/> días hasta hoy. Con solo
    /// uno, el otro sale del rango por defecto. 400 <see cref="CodigoFiltroInvalido"/> (con <c>campo</c>) si un valor se repite o
    /// no es una fecha; 400 <see cref="CodigoRangoInvalido"/> si <c>hasta &lt; desde</c>, <c>hasta</c> es posterior a hoy o el rango
    /// pasa de <see cref="DiasMaximos"/> días.
    /// </summary>
    public static RangoDias Leer(IQueryCollection query, DateOnly hoy)
    {
        ArgumentNullException.ThrowIfNull(query);
        var hasta = Dia(query, "hasta") ?? hoy;
        var desde = Dia(query, "desde") ?? hasta.AddDays(-(DiasPorDefecto - 1));
        if (hasta < desde)
            throw Invalido(CodigoRangoInvalido, "hasta no puede ser anterior a desde.", "hasta");
        if (hasta > hoy)
            throw Invalido(CodigoRangoInvalido, "hasta no puede ser posterior a hoy.", "hasta");
        var rango = new RangoDias(desde, hasta);
        if (rango.Dias > DiasMaximos)
            throw Invalido(CodigoRangoInvalido, $"El rango admite hasta {DiasMaximos} días.", "desde");
        return rango;
    }

    public static ReglaException Invalido(string codigo, string mensaje, string campo) =>
        new(codigo, mensaje, StatusCodes.Status400BadRequest) { Campo = campo };

    private static DateOnly? Dia(IQueryCollection query, string campo)
    {
        var valores = query[campo];
        if (valores.Count > 1)
            throw Invalido(CodigoFiltroInvalido, $"{campo} va una sola vez.", campo);
        var valor = valores.Count == 0 ? null : valores[0];
        if (string.IsNullOrEmpty(valor))
            return null;
        return DateOnly.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia)
            ? dia
            : throw Invalido(CodigoFiltroInvalido, $"{campo} es una fecha AAAA-MM-DD.", campo);
    }

    private static DateTimeOffset InicioDelDia(DateOnly dia) =>
        new DateTimeOffset(dia.ToDateTime(TimeOnly.MinValue), OffsetColombia).ToUniversalTime();
}
