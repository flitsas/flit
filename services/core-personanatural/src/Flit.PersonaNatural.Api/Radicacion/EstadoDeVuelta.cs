using Flit.PersonaNatural.Api.Persistence;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Radicacion;

/// <summary>
/// <c>data</c> de <c>tramites.persona_natural.solicitud_resuelta</c> v1 (HU #14050, contrato en
/// <c>contracts/asyncapi/tramites-events.v1.yaml</c>): core-api creó el trámite de la solicitud o la rechazó por una regla de
/// negocio. Sin datos personales.
/// </summary>
public sealed record SolicitudResuelta(
    Guid SolicitudId, string Resultado, Guid? ProcedureInstanceId, string? NumeroRadicado, string? Estado, string? Codigo, string? Motivo, DateTimeOffset OcurrioEn);

/// <summary>
/// <c>data</c> de <c>tramites.persona_natural.estado_cambiado</c> v1 (HU #14051): estado actual del trámite creado por el portal.
/// La subsanación es un indicador sobre <c>rechazado</c>, no un estado. <see cref="OcurrioEn"/> ordena los eventos.
/// </summary>
public sealed record EstadoDeTramiteCambiado(
    Guid SolicitudId, Guid ProcedureInstanceId, string? NumeroRadicado, string Estado, bool SubsanacionActiva, string? Motivo, DateTimeOffset OcurrioEn);

/// <summary>Constantes del camino de vuelta desde FLIT 2 (colas del servicio, tipos de evento y origen del historial).</summary>
public static class EstadoDeVueltaEventos
{
    public const string ColaResolucion = "personanatural.resolucion";
    public const string ColaEstado = "personanatural.estado";
    public const string TipoResuelta = "tramites.persona_natural.solicitud_resuelta";
    public const string TipoEstado = "tramites.persona_natural.estado_cambiado";
    public const string ResultadoCreada = "creada";
    public const string ResultadoRechazada = "rechazada";

    /// <summary>Origen de las filas de <c>status_history</c> que vienen de FLIT 2 (el portal usa <c>bff</c>).</summary>
    public const string Origen = "flit2";
}

/// <summary>
/// Espejo de la resolución de la solicitud (HU #14050/#14051): <c>en_cola → creada</c> (con el radicado y el id del trámite) o
/// <c>en_cola → rechazada_creacion</c> (con el motivo en lenguaje claro en el historial). Solo atiende a la empresa de Persona
/// Natural y solo a solicitudes que siguen <c>en_cola</c>: un evento repetido (la bandeja del SDK ya lo ignora por su
/// <c>eventId</c>), tardío o de una solicitud ya resuelta no cambia nada.
/// </summary>
public sealed class SolicitudResueltaConsumer(PersonaNaturalDb db, TimeProvider reloj) : IEventConsumer<SolicitudResuelta>
{
    public async Task HandleAsync(EventEnvelope envelope, SolicitudResuelta data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(data);
        if (envelope.TenantId != PlatformTenants.PersonaNatural)
            return;

        var s = await db.Set<SolicitudTramite>().FirstOrDefaultAsync(x => x.Id == data.SolicitudId, ct).ConfigureAwait(false);
        if (s is null || s.Estado != EstadoSolicitud.EnCola)
            return;

        string hasta;
        string motivo;
        if (string.Equals(data.Resultado, EstadoDeVueltaEventos.ResultadoCreada, StringComparison.Ordinal) && data.ProcedureInstanceId is { } tramite)
        {
            hasta = EstadoSolicitud.Creada;
            motivo = "tramite_creado";
            s.ProcedureInstanceId = tramite;
            s.ReferenceNumber = data.NumeroRadicado;
            // Los cambios de estado pueden llegar antes que la resolución; si no, el estado inicial es el del evento.
            s.EstadoTramite ??= data.Estado;
        }
        else if (string.Equals(data.Resultado, EstadoDeVueltaEventos.ResultadoRechazada, StringComparison.Ordinal))
        {
            hasta = EstadoSolicitud.RechazadaCreacion;
            motivo = data.Motivo ?? data.Codigo ?? "rechazada";
            // Un trámite rechazado al crear no es un trámite del ciudadano: nada del espejo de estado aplica.
            s.ProcedureInstanceId = null;
            s.ReferenceNumber = null;
            s.EstadoTramite = null;
            s.SubsanacionActiva = false;
            s.SubsanacionMotivo = null;
        }
        else
        {
            return; // resultado desconocido: no se inventa un estado
        }

        db.Add(new HistorialEstado
        {
            Id = Guid.CreateVersion7(), SolicitudId = s.Id, Desde = s.Estado, Hasta = hasta, Origen = EstadoDeVueltaEventos.Origen,
            Motivo = motivo, EventId = envelope.EventId, OcurrioEn = data.OcurrioEn,
        });
        s.Estado = hasta;
        s.RowVersion++;
        s.UpdatedAt = reloj.GetUtcNow();
    }
}

/// <summary>
/// Espejo de estado de vuelta (HU #14051): cada cambio de estado del trámite creado por el portal actualiza
/// <c>procedure_status</c>, <c>rectification_active</c> y <c>rectification_reason</c> y deja una fila en <c>status_history</c>
/// con la fecha del cambio en FLIT 2 (<c>ocurrioEn</c>). Eventos repetidos o fuera de orden: el espejo conserva el estado más
/// reciente por esa marca de tiempo (lo anterior se ignora); la bandeja del SDK ignora los repetidos por <c>eventId</c>.
/// Solo atiende a la empresa de Persona Natural y a trámites que el portal conoce (mismo <c>procedureInstanceId</c>).
/// </summary>
public sealed class EstadoDeTramiteConsumer(PersonaNaturalDb db, TimeProvider reloj) : IEventConsumer<EstadoDeTramiteCambiado>
{
    private const string Rechazado = "rechazado";

    public async Task HandleAsync(EventEnvelope envelope, EstadoDeTramiteCambiado data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(data);
        if (envelope.TenantId != PlatformTenants.PersonaNatural || string.IsNullOrWhiteSpace(data.Estado))
            return;

        var s = await db.Set<SolicitudTramite>().FirstOrDefaultAsync(x => x.Id == data.SolicitudId, ct).ConfigureAwait(false);
        if (s is null || s.Estado is EstadoSolicitud.RechazadaCreacion or EstadoSolicitud.Cancelada)
            return;
        if (s.ProcedureInstanceId is { } conocido && conocido != data.ProcedureInstanceId)
            return;

        // Orden por la marca de tiempo del cambio en FLIT 2. No cuentan las filas de resolución (creada / rechazada_creacion):
        // son del portal y se escriben después de los primeros cambios del trámite.
        var ultima = await db.Set<HistorialEstado>().AsNoTracking()
            .Where(h => h.SolicitudId == s.Id && h.Origen == EstadoDeVueltaEventos.Origen
                && h.Hasta != EstadoSolicitud.Creada && h.Hasta != EstadoSolicitud.RechazadaCreacion)
            .MaxAsync(h => (DateTimeOffset?)h.OcurrioEn, ct).ConfigureAwait(false);
        if (ultima is { } u && (data.OcurrioEn < u
            || (data.OcurrioEn == u && s.EstadoTramite == data.Estado && s.SubsanacionActiva == data.SubsanacionActiva)))
        {
            return;
        }

        var desde = s.EstadoTramite;
        var esRechazado = string.Equals(data.Estado, Rechazado, StringComparison.Ordinal);
        s.EstadoTramite = data.Estado;
        s.SubsanacionActiva = esRechazado && data.SubsanacionActiva;
        // El motivo es el del organismo al rechazar; al salir de «rechazado» (re-radicación) deja de aplicar.
        s.SubsanacionMotivo = esRechazado ? data.Motivo ?? s.SubsanacionMotivo : null;
        s.ProcedureInstanceId ??= data.ProcedureInstanceId;
        s.ReferenceNumber ??= data.NumeroRadicado;
        db.Add(new HistorialEstado
        {
            Id = Guid.CreateVersion7(), SolicitudId = s.Id, Desde = desde, Hasta = data.Estado, Origen = EstadoDeVueltaEventos.Origen,
            Motivo = data.Motivo, EventId = envelope.EventId, OcurrioEn = data.OcurrioEn,
        });
        s.RowVersion++;
        s.UpdatedAt = reloj.GetUtcNow();
    }
}

public static class EstadoDeVueltaExtensions
{
    /// <summary>Registra los dos consumidores del camino de vuelta desde FLIT 2 (colas propias del servicio, inbox idempotente del SDK).</summary>
    public static IServiceCollection AddEstadoDeVueltaPortal(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddFlitConsumer<PersonaNaturalDb, SolicitudResueltaConsumer, SolicitudResuelta>(
            configuration, EstadoDeVueltaEventos.ColaResolucion, producer: "tramites", [EstadoDeVueltaEventos.TipoResuelta]);
        services.AddFlitConsumer<PersonaNaturalDb, EstadoDeTramiteConsumer, EstadoDeTramiteCambiado>(
            configuration, EstadoDeVueltaEventos.ColaEstado, producer: "tramites", [EstadoDeVueltaEventos.TipoEstado]);
        return services;
    }
}
