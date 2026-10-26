using System.Diagnostics;
using Flit.Api.Identity;
using Flit.Api.Middleware;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Messaging;

namespace Flit.Consultas.Api.Bus;

/// <summary>
/// Efecto de las colas de trabajos del diagnóstico (ADR-0075, R2): por cada mensaje, una ejecución con el llamador del sobre
/// (<c>svc-&lt;producer&gt;</c> y la empresa del <c>tenantId</c>, el mismo nivel de confianza que la cabecera
/// <c>x-flit-tenant-id</c> del gRPC; el productor ya lo autenticó el SDK con <c>ProductoresPermitidos</c>) y su resultado en la
/// outbox, dentro de la transacción que el SDK abre con la bandeja. Consulta UNA sola vez (U2 = a: los reintentos los decide
/// Diagnóstico) y no lanza por errores de la consulta (<see cref="EjecutorTrabajo"/>): solo una falla de infraestructura de este
/// servicio (su base al confirmar) agota los reintentos del SDK y llega a la DLQ. El resultado lleva el <c>correlationId</c> del
/// trabajo. Hay una subclase por cola (<see cref="ConsumidorTrabajosLote"/>, <see cref="ConsumidorTrabajosExpress"/>): el SDK
/// registra un consumidor por tipo de efecto (H3).
/// </summary>
internal abstract partial class ConsumidorTrabajosDiagnostico(EjecutorTrabajo ejecutor, PublicadorResultado publicador, ILogger logger)
    : IEventConsumer<TrabajoConsultaDiagnostico>
{
    private const string Actividad = "consultas.diagnostico.trabajo";

    public async Task HandleAsync(EventEnvelope envelope, TrabajoConsultaDiagnostico data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(data);

        // La outbox toma el correlationId de la actividad en curso cuando no hay petición HTTP.
        using var actividad = new Activity(Actividad).SetTag(CorrelationIdMiddleware.TraceTag, envelope.CorrelationId).Start();
        var caller = new PlatformServiceCaller(OidcDefaults.ServiceClientPrefix + envelope.Producer, envelope.TenantId);
        var ejecucion = await ejecutor.EjecutarAsync(data, caller, ct).ConfigureAwait(false);
        publicador.Publicar(envelope.TenantId, data, ejecucion);
        Atendido(logger, data.TrabajoId, data.Fuente, ejecucion.Error is null ? ResultadoConsultaDiagnostico.Ok : ResultadoConsultaDiagnostico.Error);
    }

    [LoggerMessage(EventId = 7420, Level = LogLevel.Debug, Message = "Trabajo {TrabajoId} de {Fuente} atendido: {Estado}")]
    private static partial void Atendido(ILogger logger, Guid trabajoId, string fuente, string estado);
}

/// <summary>Cola <see cref="TrabajoConsultaDiagnostico.ColaLote"/>: los lotes, en FIFO.</summary>
internal sealed class ConsumidorTrabajosLote(EjecutorTrabajo ejecutor, PublicadorResultado publicador, ILogger<ConsumidorTrabajosLote> logger)
    : ConsumidorTrabajosDiagnostico(ejecutor, publicador, logger);

/// <summary>Cola <see cref="TrabajoConsultaDiagnostico.ColaExpress"/>: el Express, que no espera detrás de los lotes.</summary>
internal sealed class ConsumidorTrabajosExpress(EjecutorTrabajo ejecutor, PublicadorResultado publicador, ILogger<ConsumidorTrabajosExpress> logger)
    : ConsumidorTrabajosDiagnostico(ejecutor, publicador, logger);
