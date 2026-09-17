namespace Flit.Queries.Domain.Time;

/// <summary>
/// Hora de Colombia: única fuente de verdad del huso de negocio (Épica #12552, RN-10).
///
/// <para>
/// Colombia NO aplica horario de verano desde 1993, de modo que su desfase respecto a UTC es
/// constante: −05:00. Por eso el huso se construye con un <b>offset fijo</b> y nunca consultando
/// la base de husos del sistema. No es una simplificación: es la única vía que funciona aquí.
/// Los proyectos compilan con <c>InvariantGlobalization=true</c> y <c>PublishAot=true</c>
/// (<c>Directory.Build.props</c>), y bajo esa configuración
/// <c>TimeZoneInfo.FindSystemTimeZoneById("America/Bogota")</c> lanza
/// <see cref="TimeZoneNotFoundException"/>: el identificador IANA no existe sin ICU.
/// </para>
///
/// <para>
/// <b>Solo para instantes.</b> Una fecha de CALENDARIO (<see cref="DateOnly"/>: vigencias de
/// escritura, del baúl de firmas, de identidad, de SOAT y RTM) no lleva hora y no debe pasar por
/// aquí. Convertirla de huso la corre un día hacia atrás — el defecto que corrigió la HU #11194.
/// Es la excepción RN-08 de la Épica #12552.
/// </para>
/// </summary>
public static class ColombiaTime
{
    /// <summary>Desfase de Colombia respecto a UTC. Constante todo el año.</summary>
    public static readonly TimeSpan Offset = TimeSpan.FromHours(-5);

    /// <summary>
    /// El huso, para las APIs que exigen un <see cref="TimeZoneInfo"/> en vez de un offset
    /// (<c>TimeZoneInfo.ConvertTime</c>, agrupaciones por día en los repositorios de métricas).
    /// Se crea a mano, sin buscar en el sistema, por el motivo descrito arriba.
    /// </summary>
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone(
        id: "America/Bogota",
        baseUtcOffset: Offset,
        displayName: "Hora de Colombia",
        standardDisplayName: "Hora de Colombia");

    /// <summary>El mismo instante, expresado en hora de Colombia.</summary>
    public static DateTimeOffset From(DateTimeOffset instant) => instant.ToOffset(Offset);

    /// <summary>Día calendario de Colombia en el que cae <paramref name="instant"/>.</summary>
    public static DateOnly DayOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(From(instant).DateTime);

    /// <summary>Hoy en Colombia según el reloj recibido.</summary>
    public static DateOnly Today(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return DayOf(clock.GetUtcNow());
    }
}
