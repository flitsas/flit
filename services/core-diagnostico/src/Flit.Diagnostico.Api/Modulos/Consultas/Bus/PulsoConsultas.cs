namespace Flit.Diagnostico.Api.Modulos.Consultas.Bus;

/// <summary>
/// Señal de vida de core-consultas para el reconciliador (ADR-0075, 6r-i, U4 = a): el instante del último resultado que llegó a
/// este proceso (<see cref="ConsumidorResultados"/> lo marca con cada mensaje, también los descartados). Si no llegó ninguno en la
/// ventana, core-consultas está caído y los trabajos esperan en su cola: el reconciliador no los duplica ni cierra celdas en
/// ERROR. Es de la réplica (en memoria): una réplica que acaba de arrancar o que no recibe resultados se queda en pausa, del lado
/// seguro. Singleton y seguro entre hilos (el consumidor atiende varios mensajes a la vez), sin datos de las consultas.
/// </summary>
/// <example>
/// <code>
/// pulso.Registrar(reloj.GetUtcNow());
/// if (pulso.Vivo(ahora, TimeSpan.FromMinutes(10))) { /* republicar huérfanas */ }
/// </code>
/// </example>
public sealed class PulsoConsultas
{
    /// <summary>Ticks UTC del último resultado; 0 si no llegó ninguno.</summary>
    private long _ultimo;

    /// <summary>Cuándo llegó el último resultado; null si no llegó ninguno desde que arrancó el proceso.</summary>
    public DateTimeOffset? UltimoResultado => Interlocked.Read(ref _ultimo) is var ticks and > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

    /// <summary>Marca que llegó un resultado (se queda con el más reciente).</summary>
    public void Registrar(DateTimeOffset instante)
    {
        var ticks = instante.UtcTicks;
        var actual = Interlocked.Read(ref _ultimo);
        while (ticks > actual)
        {
            var visto = Interlocked.CompareExchange(ref _ultimo, ticks, actual);
            if (visto == actual)
                return;
            actual = visto;
        }
    }

    /// <summary>Verdadero si llegó algún resultado en la <paramref name="ventana"/> que termina en <paramref name="ahora"/>.</summary>
    public bool Vivo(DateTimeOffset ahora, TimeSpan ventana) => UltimoResultado is { } ultimo && ultimo >= ahora - ventana;
}
