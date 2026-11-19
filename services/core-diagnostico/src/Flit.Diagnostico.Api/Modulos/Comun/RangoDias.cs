using System.Globalization;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Comun;

/// <summary>
/// Rango de días de Colombia (UTC−5 fijo, S10) que comparten las estadísticas y los reportes (HU #14579; SP-9, SP-11; CR-BE-10).
/// <see cref="Desde"/> y <see cref="Hasta"/> cuentan los dos: <see cref="Inicio"/> es las 00:00 de <c>desde</c> y <see cref="Fin"/> las
/// 00:00 del día siguiente a <c>hasta</c>, como instantes (rango semiabierto). <see cref="Dias"/> cuenta los dos extremos, igual que el
/// front.
/// </summary>
/// <example>
/// <code>
/// RangoDias.RechazarProhibidos(query, p => $"Las estadísticas no admiten el parámetro {p}.");
/// var rango = RangoDias.Leer(query, RangoDias.Hoy(tiempo));
/// </code>
/// </example>
internal readonly record struct RangoDias(DateOnly Desde, DateOnly Hasta)
{
    /// <summary>Rango máximo en días, con los dos extremos incluidos (SP-11).</summary>
    public const int DiasMaximos = 366;

    /// <summary>Parámetros que ninguna ruta de estadísticas ni de reportes acepta (SP-9: la PII no viaja en la URL).</summary>
    private static readonly string[] Prohibidos = ["documento", "nombre"];

    public int Dias => Hasta.DayNumber - Desde.DayNumber + 1;

    public DateTimeOffset Inicio => InicioDelDia(Desde);

    public DateTimeOffset Fin => InicioDelDia(Hasta.AddDays(1));

    /// <summary>Día de Colombia de un instante.</summary>
    public static DateOnly DiaDe(DateTimeOffset instante) => DateOnly.FromDateTime(instante.ToOffset(VigenciaDelDia.OffsetColombia).DateTime);

    /// <summary>Hoy en Colombia según el reloj.</summary>
    public static DateOnly Hoy(TimeProvider reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        return DiaDe(reloj.GetUtcNow());
    }

    /// <summary>400 <c>FILTRO_INVALIDO</c> con <c>campo</c> si llega <c>documento</c> o <c>nombre</c>; el mensaje lo pone cada módulo.</summary>
    public static void RechazarProhibidos(IQueryCollection query, Func<string, string> mensaje)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(mensaje);
        foreach (var prohibido in Prohibidos)
        {
            if (query.ContainsKey(prohibido))
                throw Invalido(FiltroListado.CodigoFiltroInvalido, mensaje(prohibido), prohibido);
        }
    }

    /// <summary>
    /// Lee <c>desde</c> y <c>hasta</c>. 400 <c>FILTRO_INVALIDO</c> con <c>campo</c>: ausente, vacío, repetido o que no es AAAA-MM-DD. 400
    /// <c>RANGO_INVALIDO</c>: <c>hasta &lt; desde</c>, <c>hasta</c> posterior a <paramref name="hoy"/> o más de 366 días con los dos extremos.
    /// Los mensajes no repiten valores de la consulta.
    /// </summary>
    public static RangoDias Leer(IQueryCollection query, DateOnly hoy)
    {
        ArgumentNullException.ThrowIfNull(query);
        var desde = Dia(query, "desde");
        var hasta = Dia(query, "hasta");
        if (hasta < desde)
            throw Invalido(FiltroListado.CodigoRangoInvalido, "hasta no puede ser anterior a desde.", "hasta");
        if (hasta > hoy)
            throw Invalido(FiltroListado.CodigoRangoInvalido, "hasta no puede ser posterior a hoy.", "hasta");
        var rango = new RangoDias(desde, hasta);
        if (rango.Dias > DiasMaximos)
            throw Invalido(FiltroListado.CodigoRangoInvalido, $"El rango admite hasta {DiasMaximos} días.", "desde");
        return rango;
    }

    /// <summary>400 <c>FILTRO_INVALIDO</c> con <c>campo</c> (para las validaciones propias de cada módulo).</summary>
    public static ReglaException Invalido(string codigo, string mensaje, string campo) =>
        new(codigo, mensaje, StatusCodes.Status400BadRequest) { Campo = campo };

    private static DateOnly Dia(IQueryCollection query, string campo)
    {
        var valores = query[campo];
        if (valores.Count > 1)
            throw Invalido(FiltroListado.CodigoFiltroInvalido, $"{campo} va una sola vez.", campo);
        var valor = valores.Count == 0 ? null : valores[0];
        if (string.IsNullOrEmpty(valor))
            throw Invalido(FiltroListado.CodigoFiltroInvalido, $"{campo} es obligatorio (AAAA-MM-DD).", campo);
        return DateOnly.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia)
            ? dia
            : throw Invalido(FiltroListado.CodigoFiltroInvalido, $"{campo} es una fecha AAAA-MM-DD.", campo);
    }

    private static DateTimeOffset InicioDelDia(DateOnly dia) =>
        new DateTimeOffset(dia.ToDateTime(TimeOnly.MinValue), VigenciaDelDia.OffsetColombia).ToUniversalTime();
}
