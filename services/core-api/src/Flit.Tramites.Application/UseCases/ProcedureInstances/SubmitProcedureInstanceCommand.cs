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
    IPlatePreassignPolicy platePreassignPolicy,
    ILogger<SubmitProcedureInstanceHandler> logger)
{
    private readonly IPlatePreassignPolicy _platePolicy = platePreassignPolicy;
    private readonly ILogger<SubmitProcedureInstanceHandler> _logger = logger;

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

        // Defensa: placa completa NUNCA debe quedar en Standard/Preasignado (el OT aprobaría sin
        // paso gestor). Si la policy degradó, forzar Asignado (skip OFF es el default seguro).
        route = await EnsureFullPlateNotStandardAsync(route, id, tenantId, ct).ConfigureAwait(false);

        SubmitLog.PlateRoute(_logger, id, tenantId, route.Decision, route.Reason);

        // HU #10806 (AC4) — la compañía tiene preasignación activa pero el OT está mal configurado:
        // se BLOQUEA la radicación con un error subsanable, en vez de degradar a estándar en silencio.
        if (route.Decision == PlateRouteDecision.Blocked)
            return (null, "plate_route_misconfigured");

        var (plateFlowStatus, transitionReason) = route.Decision switch
        {
            PlateRouteDecision.Asignado => (
                PlateFlowStatus.Asignado,
                "Radicación: entregado; placa seleccionada o del RUNT (sub-estado asignado)."),
            PlateRouteDecision.Terminado => (
                PlateFlowStatus.Terminado,
                "Radicación: entregado; placa seleccionada/RUNT y paso gestor omitido (sub-estado terminado)."),
            PlateRouteDecision.Preasignado => (
                PlateFlowStatus.Preasignado,
                "Radicación: entregado sin placa; pendiente de asignación OT (sub-estado preasignado / sin asignar)."),
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

    /// <summary>
    /// Si hay field_value <c>plate</c> con valor y la policy devolvió Standard o Preasignado,
    /// corrige a Asignado para no entregar la pelota al OT sin paso gestor.
    /// </summary>
    private async Task<PlateRouteResult> EnsureFullPlateNotStandardAsync(
        PlateRouteResult route,
        Guid id,
        Guid tenantId,
        CancellationToken ct)
    {
        if (route.Decision is not (PlateRouteDecision.Standard or PlateRouteDecision.Preasignado))
            return route;

        var detail = await repo.GetByIdWithDetailsAsync(id, tenantId, ct).ConfigureAwait(false);
        if (detail is null)
            return route;

        if (!string.Equals(detail.ModalidadEntrada, "matricula_inicial", StringComparison.OrdinalIgnoreCase))
            return route;

        var plate = detail.FieldValues
            .FirstOrDefault(f => string.Equals(f.FieldKey, "plate", StringComparison.OrdinalIgnoreCase))
            ?.ValueText;
        if (string.IsNullOrWhiteSpace(plate))
            return route;

        SubmitLog.PlateRouteForcedAsignado(_logger, id, tenantId, route.Decision, route.Reason);
        return PlateRouteResult.Reserved;
    }
}

/// <summary>Logging source-generated (CA1848) del enrutamiento de placa al radicar (HU #10806).</summary>
internal static partial class SubmitLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Ruta de placa para el trámite {InstanceId} (tenant {TenantId}): {Decision} ({Reason}).")]
    public static partial void PlateRoute(
        ILogger logger, Guid instanceId, Guid tenantId, PlateRouteDecision decision, PlateRouteReason reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Ruta de placa corregida a Asignado para {InstanceId} (tenant {TenantId}): la policy devolvió {Decision} ({Reason}) con placa completa.")]
    public static partial void PlateRouteForcedAsignado(
        ILogger logger, Guid instanceId, Guid tenantId, PlateRouteDecision decision, PlateRouteReason reason);
}
