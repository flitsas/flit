using Flit.Platform.Sdk.Messaging;

namespace Flit.Impuestos.Api.Modulos.Recibos;

/// <summary>
/// Datos de <c>impuestos.recibos.descargar</c> v1 (<c>impuestos-events.v1.yaml</c>): solo el id del ítem. Lo deja M10
/// (<c>AplicadorResultado</c>) en la outbox al aplicar un resultado con archivos.
/// </summary>
public sealed record RecibosDescargar(Guid ItemId)
{
    public const string Tipo = "impuestos.recibos.descargar";
    public const int Version = 1;
    public const string Cola = "impuestos.recibos";

    /// <summary>Lo publica y lo consume el propio servicio (exchange <c>flit.impuestos</c>).</summary>
    public const string Productor = "impuestos";
}

/// <summary>Quedan recibos con una falla transitoria: el SDK pasa el mensaje a la siguiente cola de reintento.</summary>
public sealed class RecibosPorReintentarException : Exception
{
    public RecibosPorReintentarException()
    {
    }

    public RecibosPorReintentarException(string message)
        : base(message)
    {
    }

    public RecibosPorReintentarException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Consumidor de <see cref="RecibosDescargar"/> en la cola <see cref="RecibosDescargar.Cola"/> (plan §2.4 y §5.5; HU #14694 M11).
/// Solo acepta mensajes del propio servicio. Por cada mensaje descarga los recibos del ítem con <see cref="DescargaRecibos"/>, en la
/// empresa del sobre (un ítem de otra compañía no existe y el mensaje se confirma sin efecto). Si quedan recibos con una falla
/// transitoria lanza <see cref="RecibosPorReintentarException"/> y el SDK reintenta a los 1, 5 y 30 minutos
/// (<see cref="DescargaRecibos.Esperas"/>); al cuarto intento los recibos quedan <c>PENDIENTE_REINTENTO_MANUAL</c> y el mensaje se
/// confirma. Sin api_key, o con la api_key rechazada, va directo a la DLQ (<see cref="SinReintentoAutomaticoException"/>): se
/// reintenta a mano cuando la compañía tenga credenciales.
/// </summary>
/// <example>
/// <code>
/// services.AddFlitConsumer&lt;ImpuestosDb, ConsumidorRecibos, RecibosDescargar&gt;(configuration, RecibosDescargar.Cola,
///     RecibosDescargar.Productor, [RecibosDescargar.Tipo], ConsumidorRecibos.ConfigurarCola);
/// </code>
/// </example>
public sealed partial class ConsumidorRecibos(DescargaRecibos descarga, ILogger<ConsumidorRecibos> log) : IEventConsumer<RecibosDescargar>
{
    /// <summary>Esperas de reintento de la cola (1, 5 y 30 min) y solo el productor <see cref="RecibosDescargar.Productor"/>.</summary>
    public static void ConfigurarCola(PlatformConsumerOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        o.RetryDelays.Clear();
        foreach (var espera in DescargaRecibos.Esperas)
            o.RetryDelays.Add(espera);
        o.ProductoresPermitidos.Add(RecibosDescargar.Productor);
    }

    public async Task HandleAsync(EventEnvelope envelope, RecibosDescargar data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(data);
        if (envelope.TenantId == Guid.Empty)
        {
            Descartado(log, data.ItemId);
            return;
        }

        var r = await descarga.DescargarAsync(envelope.TenantId, data.ItemId, ct).ConfigureAwait(false);
        if (r.SinCredencial)
            throw new SinReintentoAutomaticoException("La compañía no tiene una api_key vigente para descargar los recibos.");
        if (r.PorReintentar > 0)
            throw new RecibosPorReintentarException($"{r.PorReintentar} recibo(s) del ítem {data.ItemId} con una falla transitoria.");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recibos: mensaje del ítem {Item} sin empresa en el sobre; se descarta.")]
    private static partial void Descartado(ILogger logger, Guid item);
}
