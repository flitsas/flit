using System.Globalization;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Estadisticas;

/// <summary>
/// Rango ya validado de E1–E3 (HU #14579, BE-1; contrato-api §0, SP-11). <see cref="Desde"/> y <see cref="Hasta"/> son días de Colombia
/// (UTC−5 fijo, S10) y los dos cuentan: <see cref="Inicio"/> es las 00:00 de <c>desde</c> y <see cref="Fin"/> las 00:00 del día siguiente a
/// <c>hasta</c>, como instantes (rango semiabierto). <see cref="Dias"/> cuenta los dos extremos, el mismo criterio del front.
/// </summary>
internal sealed record FiltroEstadisticas(DateOnly Desde, DateOnly Hasta)
{
    /// <summary>Rango máximo en días, con los dos extremos incluidos (SP-11).</summary>
    public const int DiasMaximos = 366;

    /// <summary>Parámetros que ninguna ruta de estadísticas acepta (SP-9: nada de PII en la URL).</summary>
    private static readonly string[] Prohibidos = ["documento", "nombre"];

    public int Dias => Hasta.DayNumber - Desde.DayNumber + 1;

    public DateTimeOffset Inicio => InicioDelDia(Desde);

    public DateTimeOffset Fin => InicioDelDia(Hasta.AddDays(1));

    /// <summary>Día de Colombia de un instante.</summary>
    public static DateOnly DiaDe(DateTimeOffset instante) => DateOnly.FromDateTime(instante.ToOffset(VigenciaDelDia.OffsetColombia).DateTime);

    /// <summary>
    /// Lee la consulta. 400 <c>FILTRO_INVALIDO</c> con <c>campo</c>: falta <c>desde</c> o <c>hasta</c>, no es AAAA-MM-DD, va repetido,
    /// llega <c>documento</c> o <c>nombre</c>, o llega <c>empresa</c> en una ruta que no la admite (E1 y E2). 400 <c>RANGO_INVALIDO</c>:
    /// <c>hasta &lt; desde</c>, <c>hasta</c> posterior a hoy (Colombia, según <paramref name="tiempo"/>) o más de 366 días. Los demás
    /// parámetros se ignoran. Los mensajes no repiten valores de la consulta.
    /// </summary>
    public static FiltroEstadisticas Leer(IQueryCollection query, TimeProvider tiempo, bool admiteEmpresa)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(tiempo);
        foreach (var prohibido in Prohibidos)
        {
            if (query.ContainsKey(prohibido))
                throw Invalido(FiltroListado.CodigoFiltroInvalido, $"Las estadísticas no admiten el parámetro {prohibido}.", prohibido);
        }

        if (!admiteEmpresa && query.ContainsKey(EmpresaExplicita.Parametro))
            throw Invalido(FiltroListado.CodigoFiltroInvalido, "Esta ruta no admite el parámetro empresa.", EmpresaExplicita.Parametro);

        var desde = Dia(query, "desde");
        var hasta = Dia(query, "hasta");
        if (hasta < desde)
            throw Invalido(FiltroListado.CodigoRangoInvalido, "hasta no puede ser anterior a desde.", "hasta");
        if (hasta > DiaDe(tiempo.GetUtcNow()))
            throw Invalido(FiltroListado.CodigoRangoInvalido, "hasta no puede ser posterior a hoy.", "hasta");
        var filtro = new FiltroEstadisticas(desde, hasta);
        if (filtro.Dias > DiasMaximos)
            throw Invalido(FiltroListado.CodigoRangoInvalido, $"El rango admite hasta {DiasMaximos} días.", "desde");
        return filtro;
    }

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

    private static ReglaException Invalido(string codigo, string mensaje, string campo) =>
        new(codigo, mensaje, StatusCodes.Status400BadRequest) { Campo = campo };
}
