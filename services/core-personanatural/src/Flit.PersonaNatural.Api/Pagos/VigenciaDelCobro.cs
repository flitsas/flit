namespace Flit.PersonaNatural.Api.Pagos;

/// <summary>
/// Vigencia del código de cobro en caja (Feature #14447): vale hasta las 23:59:59 hora de Colombia del día en que se habilita.
/// Colombia no tiene horario de verano, así que el desplazamiento es fijo (UTC-5) y no depende de la base de zonas horarias del
/// sistema operativo, que cambia entre Windows y Linux.
/// </summary>
public static class VigenciaDelCobro
{
    private static readonly TimeSpan Colombia = TimeSpan.FromHours(-5);

    /// <summary>Último segundo vigente: 23:59:59 (hora de Colombia) del día de <paramref name="ahora"/>, expresado en UTC (Npgsql solo escribe offset 0).</summary>
    public static DateTimeOffset FinDelDia(DateTimeOffset ahora)
    {
        var local = ahora.ToOffset(Colombia);
        return new DateTimeOffset(local.Year, local.Month, local.Day, 23, 59, 59, Colombia).ToUniversalTime();
    }

    /// <summary>Vigente mientras no haya empezado el segundo siguiente al vencimiento (la medianoche).</summary>
    public static bool EstaVigente(DateTimeOffset? venceEn, DateTimeOffset ahora) =>
        venceEn is { } vence && ahora < vence.AddSeconds(1);

    /// <summary>La fecha (día calendario) en Colombia del instante dado.</summary>
    public static DateOnly DiaColombia(DateTimeOffset ahora) => DateOnly.FromDateTime(ahora.ToOffset(Colombia).DateTime);

    /// <summary>Inicio del día de Colombia que contiene <paramref name="ahora"/>; sirve para listar «lo cobrado hoy».</summary>
    public static DateTimeOffset InicioDelDia(DateTimeOffset ahora)
    {
        var local = ahora.ToOffset(Colombia);
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, Colombia).ToUniversalTime();
    }
}
