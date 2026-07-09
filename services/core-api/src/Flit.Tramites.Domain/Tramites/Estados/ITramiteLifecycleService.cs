using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>Orden de transición de estado de un trámite (N 03). Entrada única del ciclo de vida.</summary>
/// <param name="InstanceId">Instancia a transicionar.</param>
/// <param name="TenantId">Tenant efectivo (enforcement multi-tenant).</param>
/// <param name="ToStatus">Estado destino (<see cref="TramiteEstado"/>).</param>
/// <param name="Reason">Motivo (RF05). Obligatorio para <c>anulado</c> y <c>rechazado</c>.</param>
/// <param name="ChangedByUserId">Usuario que ejecuta la transición (claim <c>sub</c>); null si es un proceso automático.</param>
public sealed record TramiteTransitionCommand(
    Guid InstanceId,
    Guid TenantId,
    string ToStatus,
    string? Reason,
    Guid? ChangedByUserId);

/// <summary>
/// Resultado de una transición. <c>ErrorCode</c> null = éxito. Los códigos son los de
/// <see cref="TramiteEstadoErrores"/> (+ los códigos de gate/OT reusados del submit:
/// <c>organismo_no_habilitado</c>, <c>ot_rule_blocked</c>, <c>biometria_requerida_ot</c>, <c>not_published</c>).
/// </summary>
public sealed record TramiteTransitionOutcome(
    ProcedureInstance? Instance,
    string? ErrorCode,
    string? ErrorDetail)
{
    public bool Success => ErrorCode is null;

    public static TramiteTransitionOutcome Ok(ProcedureInstance instance) => new(instance, null, null);

    public static TramiteTransitionOutcome Fail(string errorCode, string? detail = null) =>
        new(null, errorCode, detail);
}

/// <summary>
/// Servicio ÚNICO de ciclo de vida del trámite (ADR-0022). TODAS las transiciones de
/// <c>procedure_instances.status</c> pasan por aquí: valida la máquina (<see cref="TramiteStateMachine"/>),
/// aplica los gates de negocio (RF03: identidad aprobada + documentos obligatorios para Borrador→Preparado),
/// bloquea estados finales (RF04), registra historial vía <see cref="ITramiteTransitionRecorder"/> (RF05)
/// y encola la notificación vía <see cref="ITramiteTransitionPublisher"/> (RNF01) — todo en la MISMA
/// unidad de trabajo (un solo SaveChanges). Un conflicto de concurrencia (row_version) devuelve
/// <see cref="TramiteEstadoErrores.ConflictoConcurrencia"/> sin efectos parciales.
/// </summary>
public interface ITramiteLifecycleService
{
    Task<TramiteTransitionOutcome> TransitionAsync(TramiteTransitionCommand command, CancellationToken ct = default);
}
