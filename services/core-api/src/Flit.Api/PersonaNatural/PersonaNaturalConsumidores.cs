using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Messaging;

namespace Flit.Api.PersonaNatural;

/// <summary>
/// HU #14050 — consumidor del trabajo <c>tramites.persona_natural.radicacion_solicitada</c> que deja core-personanatural al
/// radicar el ciudadano. La bandeja del SDK ignora un <c>eventId</c> repetido y el manejador, un <c>external_ref</c> repetido:
/// reintentar el evento no duplica el trámite. Una falla técnica (servicio del portal caído, base) se propaga: el SDK
/// reintenta (10 s, 1 min, 10 min) y después deja el mensaje en <c>tramites.personanatural-radicacion.dlq</c>, visible para
/// soporte (AC5). Un rechazo de negocio NO es una falla: publica <c>solicitud_resuelta</c> con <c>rechazada</c> y su motivo.
/// </summary>
internal sealed class RadicacionSolicitadaConsumer(
    IPersonaNaturalRadicador handler,
    IPlatformOutbox outbox,
    TimeProvider reloj,
    ILogger<RadicacionSolicitadaConsumer> logger) : IEventConsumer<RadicacionSolicitadaDatos>
{
    public const string Cola = "tramites.personanatural-radicacion";

    public async Task HandleAsync(EventEnvelope envelope, RadicacionSolicitadaDatos data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(data);

        // El exchange lleva eventos de todas las empresas; este trabajo solo es de la empresa del portal.
        if (envelope.TenantId != PlatformTenants.PersonaNatural)
            return;

        var resultado = await handler.ProcesarAsync(data, ct).ConfigureAwait(false);
        if (resultado.Ignorada)
            return;

        outbox.Enqueue(
            PersonaNaturalEventos.SolicitudResuelta, 1, PlatformTenants.PersonaNatural,
            new SolicitudResueltaDatos(
                data.SolicitudId,
                resultado.Creada ? PersonaNaturalEventos.ResultadoCreada : PersonaNaturalEventos.ResultadoRechazada,
                resultado.ProcedureInstanceId,
                resultado.NumeroRadicado,
                resultado.Estado,
                resultado.Codigo,
                resultado.Motivo,
                reloj.GetUtcNow()));
        RadicacionLog.Resuelta(logger, data.SolicitudId, resultado.Creada ? PersonaNaturalEventos.ResultadoCreada : PersonaNaturalEventos.ResultadoRechazada, resultado.Codigo);
    }
}
