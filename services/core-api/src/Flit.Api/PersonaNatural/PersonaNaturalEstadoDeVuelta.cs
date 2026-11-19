using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Messaging;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.PersonaNatural;

/// <summary><c>data</c> de <c>tramites.procedure.state_changed</c> (solo lo que este consumidor necesita).</summary>
internal sealed record CambioDeEstadoDatos(Guid ProcedureInstanceId, string? FromStatus, string ToStatus, string? Reason);

/// <summary>
/// HU #14051 — espejo de estado de vuelta: cuando un trámite creado por el portal cambia de estado en FLIT 2, publica
/// <c>tramites.persona_natural.estado_cambiado</c> (estado, motivo y marca de tiempo; sin datos personales) para que
/// core-personanatural actualice su espejo. Es ADITIVO: solo escucha el evento que Trámites ya publica
/// (<c>tramites.procedure.state_changed</c>) en una cola propia, no cambia nada de lo que ya sale y no toca los trámites de
/// ninguna otra empresa. Un trámite que no nació en el portal se ignora.
/// </summary>
internal sealed class EstadoDelPortalConsumer(
    FlitDbContext db,
    IPlatformOutbox outbox,
    ILogger<EstadoDelPortalConsumer> logger) : IEventConsumer<CambioDeEstadoDatos>
{
    public const string Cola = "tramites.personanatural-estado";

    /// <summary>Estados cuyo motivo (texto del organismo) se entrega al espejo.</summary>
    private static readonly HashSet<string> EstadosConMotivo = new(StringComparer.Ordinal)
    {
        TramiteEstado.Rechazado, TramiteEstado.Anulado, TramiteEstado.Revocado,
    };

    public async Task HandleAsync(EventEnvelope envelope, CambioDeEstadoDatos data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(data);
        if (envelope.TenantId != PlatformTenants.PersonaNatural)
            return;

        // La anulación con la que core-api retira el borrador de una solicitud rechazada en la creación no es un estado del
        // ciudadano: el portal ya recibe ese rechazo por solicitud_resuelta.
        if (string.Equals(data.ToStatus, TramiteEstado.Anulado, StringComparison.Ordinal)
            && data.Reason?.StartsWith(PersonaNaturalCreador.PrefijoAnulacion, StringComparison.Ordinal) == true)
        {
            return;
        }

        var tramite = await db.Set<ProcedureInstance>().AsNoTracking()
            .Where(p => p.Id == data.ProcedureInstanceId && p.TenantId == PlatformTenants.PersonaNatural && p.Origin == PersonaNaturalEventos.Origen)
            .Select(p => new { p.ExternalRef, p.ReferenceNumber, p.SubsanacionActiva })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (tramite is null || !Guid.TryParse(tramite.ExternalRef, out var solicitudId))
            return;

        var motivo = EstadosConMotivo.Contains(data.ToStatus) ? Recortar(data.Reason) : null;
        outbox.Enqueue(
            PersonaNaturalEventos.EstadoCambiado, 1, PlatformTenants.PersonaNatural,
            new EstadoCambiadoDatos(
                solicitudId, data.ProcedureInstanceId, tramite.ReferenceNumber, data.ToStatus,
                SubsanacionActiva: string.Equals(data.ToStatus, TramiteEstado.Rechazado, StringComparison.Ordinal) && tramite.SubsanacionActiva,
                motivo, envelope.OccurredAt));
        EstadoDelPortalLog.EstadoPublicado(logger, data.ProcedureInstanceId, data.ToStatus, tramite.SubsanacionActiva);
    }

    internal static string? Recortar(string? motivo) =>
        string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim() is { Length: > 500 } largo ? largo[..500] : motivo.Trim();
}

/// <summary>
/// HU #14051 — abrir una subsanación no es una transición de estado y por eso no hay <c>state_changed</c>. Este publicador lo
/// conecta el caso de uso <c>StartSubsanacionHandler</c> y, solo para trámites del portal, deja
/// <c>estado_cambiado</c> con <c>subsanacionActiva = true</c> y el motivo del último rechazo del organismo, en la misma
/// transacción que activa el indicador.
/// </summary>
internal sealed class PersonaNaturalSubsanacionPublisher(FlitDbContext db, IPlatformOutbox outbox, TimeProvider reloj) : ITramiteSubsanacionPublisher
{
    public async Task EnqueueAsync(ProcedureInstance instance, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (instance.TenantId != PlatformTenants.PersonaNatural
            || !string.Equals(instance.Origin, PersonaNaturalEventos.Origen, StringComparison.Ordinal)
            || !Guid.TryParse(instance.ExternalRef, out var solicitudId))
        {
            return;
        }

        var motivo = await db.Set<ProcedureInstanceStatusHistory>().AsNoTracking()
            .Where(h => h.ProcedureInstanceId == instance.Id && h.ToStatus == TramiteEstado.Rechazado)
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => h.Reason)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        outbox.Enqueue(
            PersonaNaturalEventos.EstadoCambiado, 1, PlatformTenants.PersonaNatural,
            new EstadoCambiadoDatos(
                solicitudId, instance.Id, instance.ReferenceNumber, TramiteEstado.Rechazado,
                SubsanacionActiva: true, EstadoDelPortalConsumer.Recortar(motivo), reloj.GetUtcNow()));
    }
}

internal static partial class EstadoDelPortalLog
{
    [LoggerMessage(EventId = 14053, Level = LogLevel.Information, Message = "Portal Persona Natural: el trámite {ProcedureInstanceId} pasó a {Estado} (subsanación {Subsanacion}).")]
    public static partial void EstadoPublicado(ILogger logger, Guid procedureInstanceId, string estado, bool subsanacion);
}

internal static class PersonaNaturalEstadoDeVueltaExtensions
{
    /// <summary>Espejo de estado de vuelta (HU #14051): el publicador de subsanación y el consumidor de los cambios de estado.</summary>
    public static IServiceCollection AddPersonaNaturalEstadoDeVuelta(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddScoped<ITramiteSubsanacionPublisher, PersonaNaturalSubsanacionPublisher>();
        services.AddFlitConsumer<FlitDbContext, EstadoDelPortalConsumer, CambioDeEstadoDatos>(
            configuration, EstadoDelPortalConsumer.Cola, PersonaNaturalEventos.Productor, [PersonaNaturalEventos.CambioDeEstadoDeTramite]);
        return services;
    }
}
