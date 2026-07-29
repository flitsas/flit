using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Radicar (N 03, ADR-0022): orquestador delgado sobre <see cref="ITramiteLifecycleService"/>.
/// Desde <c>borrador</c> encadena <c>borrador→preparado</c> (gate RF03: identidad + documentos)
/// y <c>preparado→entregado</c> (gates OT: organismo habilitado + reglas); desde <c>preparado</c>
/// solo la entrega. Cada transición registra su fila de historial y su notificación. Si la
/// preparación pasa pero la entrega falla (p.ej. organismo_no_habilitado), el trámite queda en
/// <c>preparado</c>: corregida la causa, un nuevo submit solo reintenta la entrega.
/// <para>
/// Desde <c>subsanacion</c> (HU #10870) este MISMO handler re-radica directo a <c>entregado</c>
/// (sin encadenar preparado): <see cref="ITramiteLifecycleService"/> re-evalúa SOLO los gates de
/// negocio afectados por los campos corregidos desde el snapshot capturado al entrar a
/// subsanación (HU #10872, AC1) — más el gate final de entrega al OT, que siempre corre. La
/// identidad y las consultas externas aún vigentes NO se vuelven a solicitar (AC2).
/// </para>
/// </summary>
public sealed class SubmitProcedureInstanceHandler(
    ITramiteLifecycleService lifecycle,
    IProcedureInstanceRepository repo,
    IPlatePreassignPolicy? platePreassignPolicy = null,
    ILogger<SubmitProcedureInstanceHandler>? logger = null)
{
    private readonly IPlatePreassignPolicy _platePolicy = platePreassignPolicy ?? NullPlatePreassignPolicy.Instance;
    private readonly ILogger<SubmitProcedureInstanceHandler>? _logger = logger;

    public async Task<(ProcedureInstanceSummary? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid? changedBy,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // RF04 — estados finales (aprobado/anulado) son inmutables.
        if (TramiteEstado.EsFinal(instance.Status))
            return (null, TramiteEstadoErrores.EstadoFinal);

        // ICT (paridad v1 pauseDraftProcess / starts_procedure_in_paused): un trámite PAUSADO no avanza.
        // v1 bloqueaba con ForbiddenError salir de Borrador estando pausado; aquí se corta la radicación
        // (manual o auto-encadenada tras identidad) en el mismo punto. La anulación va por AbortDraft y NO
        // pasa por aquí, así que un trámite pausado sí se puede anular. is_paused default false ⇒ los
        // trámites de plataforma nunca entran en esta rama.
        if (instance.IsPaused)
            return (null, TramiteEstadoErrores.TramitePausado);

        // La resolución de identidad por persona (HU #10350, #87) y los gates OT viven en
        // TramiteLifecycleService — este orquestador solo encadena las transiciones.
        if (instance.Status == TramiteEstado.Borrador)
        {
            var preparado = await lifecycle.TransitionAsync(
                new TramiteTransitionCommand(
                    id, tenantId, TramiteEstado.Preparado,
                    "Radicación: gate de identidad y documentos superado.", changedBy),
                ct).ConfigureAwait(false);
            if (!preparado.Success)
                return (null, preparado.ErrorCode);
        }
        else if (!(instance.Status == TramiteEstado.Preparado
                  || TramiteEstado.EsReRadicacionSubsanacion(instance.Status, instance.SubsanacionActiva)))
        {
            return (null, TramiteEstadoErrores.TransicionNoPermitida);
        }

        // Feature #10587 / HU #10785 / HU #10806 — ruta de preasignación de placa (solo matrícula
        // inicial con la ruta activa). El status SIEMPRE queda en 'entregado' (máquina == develop); lo
        // que varía es el sub-estado INTERNO de placa: Flujo A (placa elegida y reservada) → asignado;
        // Flujo B (sin rango/placa) → preasignado; ruta estándar → null.
        var route = await _platePolicy.DecideAsync(tenantId, id, ct).ConfigureAwait(false);

        // HU #10806 (AC5) — traza observable del enrutamiento: sustituye el antiguo fallo silencioso.
        if (_logger is not null)
            SubmitLog.PlateRoute(_logger, id, tenantId, route.Decision, route.Reason);

        // HU #10806 (AC4) — la compañía tiene preasignación activa pero el OT está mal configurado:
        // se BLOQUEA la radicación con un error subsanable, en vez de degradar a estándar en silencio.
        if (route.Decision == PlateRouteDecision.Blocked)
            return (null, "plate_route_misconfigured");

        var (plateFlowStatus, transitionReason) = route.Decision switch
        {
            PlateRouteDecision.Asignado => (
                PlateFlowStatus.Asignado,
                "Radicación: entregado al OT; placa seleccionada (sub-estado asignado)."),
            PlateRouteDecision.Preasignado => (
                PlateFlowStatus.Preasignado,
                "Radicación: entregado al OT sin rango de placa; pendiente de asignación (sub-estado preasignado)."),
            _ => (
                (string?)null,
                "Radicación: trámite entregado al organismo de tránsito."),
        };

        var final = await lifecycle.TransitionAsync(
            new TramiteTransitionCommand(id, tenantId, TramiteEstado.Entregado, transitionReason, changedBy, plateFlowStatus),
            ct).ConfigureAwait(false);
        if (!final.Success)
            return (null, final.ErrorCode);

        return (CreateProcedureInstanceHandler.ToSummary(final.Instance!), null);
    }
}

/// <summary>Logging source-generated (CA1848) del enrutamiento de placa al radicar (HU #10806).</summary>
internal static partial class SubmitLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Ruta de placa para el trámite {InstanceId} (tenant {TenantId}): {Decision} ({Reason}).")]
    public static partial void PlateRoute(
        ILogger logger, Guid instanceId, Guid tenantId, PlateRouteDecision decision, PlateRouteReason reason);
}
