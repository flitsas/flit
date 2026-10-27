using System.Buffers.Binary;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Bus;

/// <summary>
/// Señales de core-consultas para el reconciliador (ADR-0075, 6r-i, U4 = a, Dec. 6), marcadas por <see cref="ConsumidorResultados"/>:
/// <list type="bullet">
/// <item><b>Pulso</b> (<see cref="Registrar"/>, <see cref="Vivo"/>): el instante del último resultado que llegó a este proceso,
/// también de los descartados. Si no llegó ninguno en la ventana, core-consultas está caído y los trabajos esperan en su cola: el
/// reconciliador no los duplica ni cierra celdas en ERROR.</item>
/// <item><b>Marca de agua FIFO por cola</b> (<see cref="RegistrarAtendido"/>, <see cref="UltimoAtendido"/>): el instante de
/// publicación (<see cref="PublicadoEn"/>, el UUIDv7 del <c>trabajo_id</c>) del trabajo más reciente cuyo resultado devolvió
/// core-consultas, aparte para la cola del lote y la del Express. Las colas son FIFO: si ya volvió un trabajo publicado bastante
/// después que el de una celda, el de la celda ya debió salir de la cola; si no, espera su turno.</item>
/// </list>
/// Son de la réplica (en memoria) y monótonas: una réplica que acaba de arrancar o que no recibe resultados no tiene marca y se
/// queda del lado seguro (no republica; solo cierra lo vencido por TTL). Singleton y seguro entre hilos (el consumidor atiende
/// varios mensajes a la vez), sin datos de las consultas.
/// </summary>
/// <example>
/// <code>
/// pulso.Registrar(reloj.GetUtcNow());
/// pulso.RegistrarAtendido(esExpress: false, resultado.TrabajoId);
/// if (pulso.UltimoAtendido(esExpress: false) is { } marca &amp;&amp; marca &gt; celda.DisponibleDesde + tolerancia) { /* perdida */ }
/// </code>
/// </example>
public sealed class PulsoConsultas
{
    /// <summary>Ticks UTC del último resultado; 0 si no llegó ninguno.</summary>
    private long _ultimo;

    /// <summary>Ticks UTC de publicación del trabajo atendido más reciente de la cola del lote; 0 si ninguno.</summary>
    private long _atendidoLote;

    /// <summary>Lo mismo para la cola del Express.</summary>
    private long _atendidoExpress;

    /// <summary>Cuándo llegó el último resultado; null si no llegó ninguno desde que arrancó el proceso.</summary>
    public DateTimeOffset? UltimoResultado => Leer(ref _ultimo);

    /// <summary>Marca que llegó un resultado (se queda con el más reciente).</summary>
    public void Registrar(DateTimeOffset instante) => Subir(ref _ultimo, instante.UtcTicks);

    /// <summary>Verdadero si llegó algún resultado en la <paramref name="ventana"/> que termina en <paramref name="ahora"/>.</summary>
    public bool Vivo(DateTimeOffset ahora, TimeSpan ventana) => UltimoResultado is { } ultimo && ultimo >= ahora - ventana;

    /// <summary>
    /// Marca que core-consultas devolvió el resultado del trabajo <paramref name="trabajoId"/> de la cola del lote o del Express (se
    /// queda con la publicación más reciente). Un id que no es UUIDv7 no la mueve.
    /// </summary>
    public void RegistrarAtendido(bool esExpress, Guid trabajoId)
    {
        if (PublicadoEn(trabajoId) is { } publicado)
            Subir(ref Marca(esExpress), publicado.UtcTicks);
    }

    /// <summary>Publicación del trabajo atendido más reciente de esa cola; null si esta réplica no vio ninguno.</summary>
    public DateTimeOffset? UltimoAtendido(bool esExpress) => Leer(ref Marca(esExpress));

    /// <summary>
    /// El instante de publicación que lleva un <c>trabajo_id</c> UUIDv7 (los 48 bits altos: milisegundos Unix, RFC 9562); null si no
    /// es v7. <see cref="PublicadorCeldas"/> lo crea con el reloj inyectado, el mismo de <c>disponible_desde</c>.
    /// </summary>
    public static DateTimeOffset? PublicadoEn(Guid trabajoId)
    {
        if (trabajoId.Version != 7)
            return null;
        Span<byte> bytes = stackalloc byte[16];
        trabajoId.TryWriteBytes(bytes, bigEndian: true, out _);
        var ms = (long)(BinaryPrimitives.ReadUInt64BigEndian(bytes) >> 16);
        return DateTimeOffset.FromUnixTimeMilliseconds(ms);
    }

    private ref long Marca(bool esExpress) => ref esExpress ? ref _atendidoExpress : ref _atendidoLote;

    private static DateTimeOffset? Leer(ref long campo) =>
        Interlocked.Read(ref campo) is var ticks and > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

    private static void Subir(ref long campo, long ticks)
    {
        var actual = Interlocked.Read(ref campo);
        while (ticks > actual)
        {
            var visto = Interlocked.CompareExchange(ref campo, ticks, actual);
            if (visto == actual)
                return;
            actual = visto;
        }
    }
}
