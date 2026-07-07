using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;

/// <summary>
/// Servicio ÚNICO de ciclo de vida del trámite (N 03, ADR-0022). Toda transición de
/// <c>procedure_instances.status</c> pasa por aquí: máquina (RF02), estados finales (RF04),
/// gate de preparación (RF03: identidad + documentos, <see cref="SubmitGate"/>), gates OT de
/// entrega (organismo habilitado + reglas OT, heredados del submit), motivo obligatorio para
/// anular/rechazar (RF05), historial (<see cref="ITramiteTransitionRecorder"/>) y publicación
/// (<see cref="ITramiteTransitionPublisher"/>) en la MISMA unidad de trabajo — un solo
/// SaveChanges con guarda de concurrencia por <c>row_version</c> (RNF01: conflicto → sin
/// efectos parciales).
/// </summary>
public sealed class TramiteLifecycleService(
    IProcedureInstanceRepository repo,
    IProcedureTypeRepository typeRepo,
    ITransitOfficeGrantGate transitOfficeGrantGate,
    IOtOperabilityGate otOperabilityGate,
    IOtRuleGate otRuleGate,
    ITramiteTransitionRecorder recorder,
    ITramiteTransitionPublisher publisher) : ITramiteLifecycleService
{
    public async Task<TramiteTransitionOutcome> TransitionAsync(
        TramiteTransitionCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TramiteEstado.EsValido(command.ToStatus))
            return TramiteTransitionOutcome.Fail(
                TramiteEstadoErrores.EstadoDesconocido,
                $"'{command.ToStatus}' no es un estado de trámite conocido.");

        var instance = await repo.GetByIdWithWizardGraphAsync(command.InstanceId, command.TenantId, ct);
        if (instance is null)
            return TramiteTransitionOutcome.Fail(TramiteEstadoErrores.NoEncontrado);

        var from = instance.Status;

        // RF04 — aprobado/anulado son inmutables: ni transiciones ni edición de datos.
        if (TramiteEstado.EsFinal(from))
            return TramiteTransitionOutcome.Fail(
                TramiteEstadoErrores.EstadoFinal,
                $"El trámite está en estado final '{from}' y no admite más transiciones.");

        if (!TramiteStateMachine.IsValidTransition(from, command.ToStatus))
            return TramiteTransitionOutcome.Fail(
                TramiteEstadoErrores.TransicionNoPermitida,
                $"La transición de '{from}' a '{command.ToStatus}' no está permitida.");

        // RF05 — anular/rechazar exigen motivo explícito para el historial.
        if (command.ToStatus is TramiteEstado.Anulado or TramiteEstado.Rechazado
            && string.IsNullOrWhiteSpace(command.Reason))
            return TramiteTransitionOutcome.Fail(
                TramiteEstadoErrores.MotivoRequerido,
                $"Debe indicar el motivo para pasar el trámite a '{command.ToStatus}'.");

        // RF03 — gate borrador→preparado: identidad aprobada/vigente + documentos obligatorios.
        if (from == TramiteEstado.Borrador && command.ToStatus == TramiteEstado.Preparado)
        {
            // Identidad PER-PERSONA (documento del actor), referenciada de su validación vigente
            // (HU #10350 rediseño #87): fila propia del trámite O identidad vigente de la persona
            // en otro trámite del tenant, sin clonar.
            var identidadAprobada = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(
                repo, instance, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
            var gateErrors = SubmitGate.Evaluate(instance, identidadAprobada);
            if (gateErrors.Count > 0)
                return TramiteTransitionOutcome.Fail(gateErrors[0], DetalleGatePreparacion(gateErrors[0]));
        }

        // Gates OT de entrega (heredados del submit HU #10217/#2).
        if (command.ToStatus == TramiteEstado.Entregado)
        {
            var entregaError = await EvaluarEntregaAsync(instance, ct).ConfigureAwait(false);
            if (entregaError is var (code, detail) && code is not null)
                return TramiteTransitionOutcome.Fail(code, detail);
        }

        var now = DateTimeOffset.UtcNow;
        instance.Status = command.ToStatus;
        instance.UpdatedAt = now;
        if (command.ToStatus == TramiteEstado.Entregado)
            instance.SubmittedAt = now;

        var record = new TramiteTransitionRecord(
            command.TenantId,
            instance.Id,
            from,
            command.ToStatus,
            command.Reason,
            command.ChangedByUserId,
            now);

        // Historial (RF05) + publicación (RNF01) se ENCOLAN en la misma unidad de trabajo;
        // el commit único de abajo los persiste o descarta en bloque.
        await recorder.RecordAsync(record, ct).ConfigureAwait(false);
        await publisher.EnqueueAsync(record, ct).ConfigureAwait(false);

        var committed = await repo.SaveChangesWithConcurrencyGuardAsync(ct).ConfigureAwait(false);
        if (!committed)
            return TramiteTransitionOutcome.Fail(
                TramiteEstadoErrores.ConflictoConcurrencia,
                "El trámite fue modificado por otro proceso. Recargue el trámite e intente de nuevo.");

        return TramiteTransitionOutcome.Ok(instance);
    }

    /// <summary>Causa exacta del gate de preparación (RF03) para el mensaje al usuario.</summary>
    private static string DetalleGatePreparacion(string code) => code switch
    {
        TramiteEstadoErrores.IdentidadNoAprobada =>
            "La validación de identidad del comprador no está aprobada o no está vigente.",
        TramiteEstadoErrores.DocumentosIncompletos =>
            "Faltan documentos obligatorios del checklist para preparar el trámite.",
        _ => "El trámite no cumple los requisitos para prepararse.",
    };

    /// <summary>
    /// Gates de la entrega al OT: tipo publicado, organismo elegido HABILITADO para la empresa
    /// (promueve <c>TransitOfficeId</c> desde field_values) y reglas OT. (null, null) = puede entregar.
    /// </summary>
    private async Task<(string? Code, string? Detail)> EvaluarEntregaAsync(
        ProcedureInstance instance,
        CancellationToken ct)
    {
        var procedureType = await typeRepo.GetByIdAsync(instance.ProcedureTypeId, ct).ConfigureAwait(false);
        if (procedureType is null || procedureType.PublicationStatus != PublicationStatus.Published)
            return ("not_published", "El tipo de trámite no está publicado.");

        // #2 — el OT elegido en el FUR (transit_office_id en field_values) debe estar HABILITADO
        // para la empresa. Se promueve a la columna TransitOfficeId para que el motor de reglas OT
        // y los listados operen sobre el id real.
        var selectedOfficeId = TransitOfficeIdFromFieldValues(instance);
        if (selectedOfficeId is { } officeId)
        {
            var enabled = await transitOfficeGrantGate
                .IsEnabledForTenantAsync(instance.TenantId, officeId, ct)
                .ConfigureAwait(false);
            if (!enabled)
                return ("organismo_no_habilitado",
                    "El organismo de tránsito seleccionado no está habilitado para la compañía.");

            // HU #10518 — con grant, pero el OT debe estar OPERATIVO en la plataforma:
            // catálogo activo + tenant OT existente y activo. Desactivar el OT (is_active=false)
            // bloquea la radicación aunque el grant siga vigente (no se revoca automáticamente).
            var operable = await otOperabilityGate
                .IsOperableAsync(officeId, ct)
                .ConfigureAwait(false);
            if (!operable)
                return ("organismo_no_operable",
                    "El organismo de tránsito no está operativo en FLIT.");

            instance.TransitOfficeId = officeId;
        }

        var ruleResult = await otRuleGate.EvaluateSubmissionAsync(
            instance.TransitOfficeId,
            instance.ProcedureTypeId,
            procedureType.Code,
            ct).ConfigureAwait(false);

        if (ruleResult.IsBlocked)
            return (ruleResult.ErrorCode ?? "ot_rule_blocked",
                "El trámite está bloqueado por una regla OT activa.");

        return (null, null);
    }

    /// <summary>
    /// Id del organismo de tránsito elegido en el FUR, leído del field_value
    /// <c>transit_office_id</c> (lo persiste el wizard al seleccionar). <c>null</c> si no hay
    /// selección o no es un GUID válido (p. ej. instancias previas a la persistencia del id).
    /// </summary>
    private static Guid? TransitOfficeIdFromFieldValues(ProcedureInstance instance)
    {
        var raw = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, "transit_office_id", StringComparison.OrdinalIgnoreCase))?.ValueText;

        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
