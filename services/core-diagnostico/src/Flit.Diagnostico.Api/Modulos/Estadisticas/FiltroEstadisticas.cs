using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;

namespace Flit.Diagnostico.Api.Modulos.Estadisticas;

/// <summary>
/// Rango ya validado de E1–E3 (HU #14579, BE-1; contrato-api §0, SP-11). <see cref="Desde"/> y <see cref="Hasta"/> son días de Colombia
/// (UTC−5 fijo, S10) y los dos cuentan: <see cref="Inicio"/> es las 00:00 de <c>desde</c> y <see cref="Fin"/> las 00:00 del día siguiente a
/// <c>hasta</c>, como instantes (rango semiabierto). <see cref="Dias"/> cuenta los dos extremos, el mismo criterio del front. El parseo
/// y las reglas del rango son los de <see cref="RangoDias"/>, compartidos con los reportes (CR-BE-10).
/// </summary>
internal sealed record FiltroEstadisticas(DateOnly Desde, DateOnly Hasta)
{
    /// <summary>Rango máximo en días, con los dos extremos incluidos (SP-11).</summary>
    public const int DiasMaximos = RangoDias.DiasMaximos;

    private RangoDias Rango => new(Desde, Hasta);

    public int Dias => Rango.Dias;

    public DateTimeOffset Inicio => Rango.Inicio;

    public DateTimeOffset Fin => Rango.Fin;

    /// <summary>Día de Colombia de un instante.</summary>
    public static DateOnly DiaDe(DateTimeOffset instante) => RangoDias.DiaDe(instante);

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
        RangoDias.RechazarProhibidos(query, prohibido => $"Las estadísticas no admiten el parámetro {prohibido}.");
        if (!admiteEmpresa && query.ContainsKey(EmpresaExplicita.Parametro))
            throw RangoDias.Invalido(FiltroListado.CodigoFiltroInvalido, "Esta ruta no admite el parámetro empresa.", EmpresaExplicita.Parametro);

        var rango = RangoDias.Leer(query, RangoDias.Hoy(tiempo));
        return new FiltroEstadisticas(rango.Desde, rango.Hasta);
    }
}
