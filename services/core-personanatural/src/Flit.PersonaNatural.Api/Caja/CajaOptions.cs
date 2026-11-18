namespace Flit.PersonaNatural.Api.Caja;

/// <summary>
/// Portal de caja del organismo (Feature #14453, ADR-0082). Sección <c>Caja</c>; la bandera también se acepta como
/// <c>PersonaNatural:Caja:Habilitado</c>. Apagado por defecto: sin encenderlo no existe ninguna ruta de caja.
/// </summary>
public sealed class CajaOptions
{
    public const string SectionName = "Caja";
    public const string ClaveHabilitadoAlterna = "PersonaNatural:Caja:Habilitado";

    public bool Habilitado { get; set; }

    /// <summary>Minutos que un cobro en caja espera, ya pagado, antes de radicarse: es la ventana en que el administrador del OT puede anularlo. 0 radica de inmediato.</summary>
    public int VentanaAnulacionMinutos { get; set; } = 15;

    /// <summary>Búsquedas sin resultado por usuario dentro de <see cref="BusquedasFallidasVentanaMin"/> antes del 429.</summary>
    public int BusquedasFallidasMax { get; set; } = 10;

    public int BusquedasFallidasVentanaMin { get; set; } = 5;

    /// <summary>Búsquedas sin resultado desde la misma IP (huella) dentro del organismo en la misma ventana. Nunca menor que el tope por usuario: varias cajas pueden compartir una IP.</summary>
    public int BusquedasFallidasMaxPorIp { get; set; } = 30;

    /// <summary>
    /// Margen, en segundos, que el trabajo de radicación espera DESPUÉS de vencida la ventana de anulación antes de radicar: en el
    /// borde exacto gana la anulación (que usa el límite sin margen), no la radicación.
    /// </summary>
    public int RadicacionGraciaSegundos { get; set; } = 5;

    /// <summary>Edad mínima, en horas, de una prefactura sin cobro pagado para que la retención la barra como huérfana.</summary>
    public int PrefacturaHuerfanaHoras { get; set; } = 24;

    /// <summary>Tope de la prefactura (PDF, JPG o PNG).</summary>
    public long PrefacturaMaxBytes { get; set; } = 5L * 1024 * 1024;

    /// <summary>Cada cuánto el trabajo de fondo radica los cobros cuya ventana venció.</summary>
    public int RadicacionIntervaloSegundos { get; set; } = 30;

    /// <summary>Espera antes del primer ciclo del trabajo de fondo.</summary>
    public int RadicacionArranqueSegundos { get; set; } = 20;

    /// <summary>Solicitudes que un ciclo intenta radicar como máximo.</summary>
    public int RadicacionLote { get; set; } = 50;

    /// <summary>Ventana de anulación efectiva. Con la caja apagada (<c>Caja:Habilitado=false</c>) es 0: todo se radica de inmediato, como antes de la caja.</summary>
    public TimeSpan Ventana => Habilitado ? TimeSpan.FromMinutes(Math.Max(0, VentanaAnulacionMinutos)) : TimeSpan.Zero;

    /// <summary>Margen configurable (<see cref="RadicacionGraciaSegundos"/>) que el trabajo de radicación suma a la ventana: en el borde exacto gana la anulación.</summary>
    public TimeSpan Gracia => TimeSpan.FromSeconds(Math.Max(0, RadicacionGraciaSegundos));

    public static CajaOptions Leer(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var o = new CajaOptions();
        configuration.GetSection(SectionName).Bind(o);
        if (configuration.GetSection(SectionName)["Habilitado"] is null && bool.TryParse(configuration[ClaveHabilitadoAlterna], out var alterna))
            o.Habilitado = alterna;
        return o;
    }
}
