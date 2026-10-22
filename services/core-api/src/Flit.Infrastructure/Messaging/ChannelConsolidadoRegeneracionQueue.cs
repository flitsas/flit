using System.Threading.Channels;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Messaging;

/// <summary>HU #12795 — clave de coalescing de la regeneración anticipada.</summary>
internal readonly record struct SolicitudRegeneracion(
    Guid TenantId,
    Guid ProcedureInstanceId,
    TipoConsolidado Documento);

/// <summary>
/// HU #12795 — <see cref="IConsolidadoRegeneracionQueue"/> sobre un <see cref="Channel"/> acotado
/// (mismo patrón que <c>ChannelUsageEventQueue</c>, ADR-0024: sin brokers ni schedulers externos).
///
/// <para>Modo <see cref="BoundedChannelFullMode.Wait"/> a propósito: con él <c>TryWrite</c> devuelve
/// <c>false</c> cuando el canal está lleno, sin bloquear. Los modos <c>Drop*</c> devuelven <c>true</c>
/// aunque descarten, y el llamador no sabría que su solicitud se perdió.</para>
///
/// <para>Singleton; el lado lector lo consume <see cref="ConsolidadoRegeneracionProcessor"/>. La cola
/// vive en memoria del proceso: un reinicio pierde lo encolado, lo que es aceptable porque el camino
/// perezoso reconstruye el PDF en la siguiente lectura (la bandera sigue abajo).</para>
/// </summary>
internal sealed class ChannelConsolidadoRegeneracionQueue : IConsolidadoRegeneracionQueue
{
    private readonly Channel<SolicitudRegeneracion> _channel;
    private readonly ILogger _logger;

    public ChannelConsolidadoRegeneracionQueue(
        IOptions<ConsolidadoRegeneracionOptions> options,
        ILogger<ChannelConsolidadoRegeneracionQueue>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var capacidad = Math.Max(1, options.Value.Capacidad);
        _channel = Channel.CreateBounded<SolicitudRegeneracion>(new BoundedChannelOptions(capacidad)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        _logger = logger ?? NullLogger<ChannelConsolidadoRegeneracionQueue>.Instance;
    }

    public bool Encolar(Guid tenantId, Guid procedureInstanceId, TipoConsolidado documento)
    {
        if (tenantId == Guid.Empty || procedureInstanceId == Guid.Empty || !Enum.IsDefined(documento))
            return false;

        if (_channel.Writer.TryWrite(new SolicitudRegeneracion(tenantId, procedureInstanceId, documento)))
            return true;

        ConsolidadoRegeneracionLog.ColaLlena(_logger, documento, procedureInstanceId, tenantId);
        return false;
    }

    /// <summary>Espera (cancelable) a que haya al menos una solicitud por leer.</summary>
    internal ValueTask<bool> WaitToReadAsync(CancellationToken ct) => _channel.Reader.WaitToReadAsync(ct);

    /// <summary>Extrae la siguiente solicitud pendiente, si la hay.</summary>
    internal bool TryDequeue(out SolicitudRegeneracion solicitud) => _channel.Reader.TryRead(out solicitud);
}
