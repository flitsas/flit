using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Radicar (N 03, ADR-0022): orquestador delgado sobre <see cref="ITramiteLifecycleService"/>.
/// Desde <c>borrador</c> encadena <c>borrador→preparado</c> (gate RF03: identidad + documentos)
/// y <c>preparado→entregado</c> (gates OT: organismo habilitado + reglas); desde <c>preparado</c>
/// solo la entrega. Cada transición registra su fila de historial y su notificación. Si la
/// preparación pasa pero la entrega falla (p.ej. organismo_no_habilitado), el trámite queda en
/// <c>preparado</c>: corregida la causa, un nuevo submit solo reintenta la entrega.
/// </summary>
public sealed class SubmitProcedureInstanceHandler(
    ITramiteLifecycleService lifecycle,
    IProcedureInstanceRepository repo,
    IPlatePreassignPolicy? platePreassignPolicy = null)
{
    private readonly IPlatePreassignPolicy _platePolicy = platePreassignPolicy ?? NullPlatePreassignPolicy.Instance;

    public async Task<(ProcedureInstanceSummary? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid? changedBy,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

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

        // Feature #10587 / HU #10785 — ruta de preasignación de placa (solo matrícula inicial con la
        // ruta activa). El status SIEMPRE queda en 'entregado' (máquina de estados == develop); lo que
        // varía es el sub-estado INTERNO de placa: Flujo A (placa elegida y reservada) → asignado;
        // Flujo B (sin rango/placa) → preasignado; ruta estándar → null.
        var decision = await _platePolicy.DecideAsync(tenantId, id, ct).ConfigureAwait(false);
        var (plateFlowStatus, reason) = decision switch
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
            new TramiteTransitionCommand(id, tenantId, TramiteEstado.Entregado, reason, changedBy, plateFlowStatus),
            ct).ConfigureAwait(false);
        if (!final.Success)
            return (null, final.ErrorCode);

        return (CreateProcedureInstanceHandler.ToSummary(final.Instance!), null);
    }
}
