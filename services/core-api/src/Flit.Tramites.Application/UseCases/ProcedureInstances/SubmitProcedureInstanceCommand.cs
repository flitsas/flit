using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

public sealed class SubmitProcedureInstanceHandler(
    IProcedureInstanceRepository repo,
    IProcedureTypeRepository typeRepo,
    IProcedureStateChangeNotifier stateChangeNotifier,
    IOtRuleGate otRuleGate,
    ITransitOfficeGrantGate transitOfficeGrantGate)
{
    public async Task<(ProcedureInstanceSummary? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid? changedBy,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithWizardGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        if (instance.Status != ProcedureInstanceStatus.Draft)
            return (null, "not_draft");

        var procedureType = await typeRepo.GetByIdAsync(instance.ProcedureTypeId, ct);
        if (procedureType is null || procedureType.PublicationStatus != PublicationStatus.Published)
            return (null, "not_published");

        // Identidad PER-PERSONA (documento del actor), referenciada de la validación vigente (HU #10350):
        // el gate no exige fila propia del trámite; basta con que la persona tenga identidad vigente aprobada.
        var identidadAprobada = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(
            repo, instance, DateTimeOffset.UtcNow, ct);
        var gateErrors = SubmitGate.Evaluate(instance, identidadAprobada);
        if (gateErrors.Count > 0)
            return (null, gateErrors[0]);

        // #2 — el OT elegido en el FUR (transit_office_id en field_values) debe estar
        // HABILITADO para la empresa. Además se promueve a la columna TransitOfficeId para
        // que el motor de reglas OT y los listados operen sobre el id real (antes el FUR
        // solo persistía field_values de texto y la columna quedaba en null).
        var selectedOfficeId = TransitOfficeIdFromFieldValues(instance);
        if (selectedOfficeId is { } officeId)
        {
            var enabled = await transitOfficeGrantGate
                .IsEnabledForTenantAsync(tenantId, officeId, ct)
                .ConfigureAwait(false);
            if (!enabled)
                return (null, "organismo_no_habilitado");

            instance.TransitOfficeId = officeId;
        }

        var ruleResult = await otRuleGate.EvaluateSubmissionAsync(
            instance.TransitOfficeId,
            instance.ProcedureTypeId,
            procedureType.Code,
            ct).ConfigureAwait(false);

        if (ruleResult.IsBlocked)
        {
            return (null, ruleResult.ErrorCode ?? "ot_rule_blocked");
        }

        var now = DateTimeOffset.UtcNow;
        instance.Status = ProcedureInstanceStatus.Submitted;
        instance.SubmittedAt = now;
        instance.UpdatedAt = now;

        // HU #10431 — la radicación queda atribuida al gestor autenticado en status_history para
        // alimentar la productividad de la analítica. Guarda FK: si el sujeto no existe en
        // identity.users (proceso automático o sub inválido), changed_by se resuelve a null sin
        // romper la inserción ni generar productividad espuria.
        var resolvedChangedBy = changedBy is { } cb && cb != Guid.Empty
            && await repo.UserExistsAsync(cb, ct).ConfigureAwait(false)
            ? changedBy
            : null;

        var statusHistory = new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            FromStatus = ProcedureInstanceStatus.Draft,
            ToStatus = ProcedureInstanceStatus.Submitted,
            ChangedAt = now,
            ChangedBy = resolvedChangedBy
        };
        instance.StatusHistory.Add(statusHistory);
        repo.Add(statusHistory);

        await repo.SaveChangesAsync(ct);

        await stateChangeNotifier.NotifyAsync(
            new ProcedureStateChangeEvent(
                tenantId,
                id,
                ProcedureInstanceStatus.Draft,
                ProcedureInstanceStatus.Submitted,
                now),
            ct).ConfigureAwait(false);

        return (CreateProcedureInstanceHandler.ToSummary(instance), null);
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
