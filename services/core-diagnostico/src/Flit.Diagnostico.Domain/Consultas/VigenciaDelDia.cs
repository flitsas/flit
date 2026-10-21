namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>
/// Vigencia del dato de una celda para la reutilización del mismo día (HU #13739, F03-H7, D-10 «b con corte a medianoche»): un
/// dato vale desde que se obtuvo hasta las 23:59:59 del MISMO día en hora Colombia y vence a las 00:00:00 del día siguiente,
/// aunque no hayan pasado 24 h. La hora Colombia es un offset fijo de UTC−5 (<see cref="OffsetColombia"/>): <c>America/Bogota</c>
/// no tiene horario de verano desde 1993, y el offset fijo no depende de la base de zonas horarias del sistema operativo
/// (Windows usa «SA Pacific Standard Time», Linux la IANA), así que el corte es el mismo en cualquier host. El instante lo da
/// quien llama (reloj inyectable, <see cref="TimeProvider"/>): la regla no mira el reloj del sistema.
/// </summary>
/// <example>
/// <code>
/// var desde = VigenciaDelDia.InicioDelDia(reloj.GetUtcNow());          // 00:00 de hoy en hora Colombia
/// var vale = VigenciaDelDia.Vigente(celda.DatoObtenidoEn!.Value, reloj.GetUtcNow());
/// </code>
/// </example>
public static class VigenciaDelDia
{
    /// <summary>Offset de la hora Colombia (<c>America/Bogota</c>): UTC−5 todo el año.</summary>
    public static readonly TimeSpan OffsetColombia = TimeSpan.FromHours(-5);

    /// <summary>Las 00:00:00 hora Colombia del día de <paramref name="ahora"/>, con offset UTC−5.</summary>
    public static DateTimeOffset InicioDelDia(DateTimeOffset ahora)
    {
        var local = ahora.ToOffset(OffsetColombia);
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, OffsetColombia);
    }

    /// <summary>Las 00:00:00 hora Colombia del día siguiente al de <paramref name="ahora"/>: desde ahí el dato de hoy ya venció.</summary>
    public static DateTimeOffset FinDelDia(DateTimeOffset ahora) => InicioDelDia(ahora).AddDays(1);

    /// <summary>Verdadero si el dato obtenido en <paramref name="obtenidoEn"/> es del mismo día, en hora Colombia, que <paramref name="ahora"/>.</summary>
    public static bool Vigente(DateTimeOffset obtenidoEn, DateTimeOffset ahora) => InicioDelDia(obtenidoEn) == InicioDelDia(ahora);
}
