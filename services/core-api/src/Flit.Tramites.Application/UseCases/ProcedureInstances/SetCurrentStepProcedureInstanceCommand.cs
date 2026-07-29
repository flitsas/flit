using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #10879 — persiste el AVANCE del borrador por pasos (autosave del paso actual del wizard). Guarda
/// la <c>Key</c> del paso donde quedó el operador en <c>procedure_instances.current_step</c>, para que al
/// reabrir el borrador el wizard retome ese punto (AC2). Reglas (AC1):
/// <list type="bullet">
///   <item>El trámite debe estar en <c>borrador</c> (fuera de borrador ya no se avanza) → <c>not_draft</c>.</item>
///   <item>La consulta del vehículo debe estar completa (VIN/placa) antes de aceptar el avance →
///   <c>vehiculo_no_consultado</c>.</item>
///   <item>El paso debe ser una <c>Key</c> legítima del wizard de la modalidad → <c>step_invalid</c>.</item>
/// </list>
/// Idempotente: si el paso ya es el pedido, no re-persiste. Solo toca la columna del trámite (NO
/// field_values), por lo que no interviene el trigger de inmutabilidad de field_values.
/// </summary>
public sealed class SetCurrentStepProcedureInstanceHandler(IProcedureInstanceRepository repo)
{
    public const string NotFound = "not_found";
    public const string NotDraft = "not_draft";
    public const string VehiculoNoConsultado = "vehiculo_no_consultado";
    public const string StepInvalid = "step_invalid";

    public async Task<(SetCurrentStepResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        string? step,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(step))
            return (null, StepInvalid);

        var instance = await repo.GetByIdWithDetailsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, NotFound);

        // El avance se persiste mientras el trámite es editable (borrador o rechazado con
        // subsanación activa). Fuera de ahí el wizard es de solo lectura.
        if (!TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva))
            return (null, NotDraft);

        // AC1 — solo se puede avanzar de paso una vez que la consulta del vehículo está completa.
        if (!GetWizardStateHandler.VehiculoConsultado(instance))
            return (null, VehiculoNoConsultado);

        // El paso debe ser una Key real del wizard de la modalidad (no se persiste basura).
        var normalized = step.Trim();
        var validKeys = GetWizardStateHandler.StepKeysFor(instance);
        if (!validKeys.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            return (null, StepInvalid);

        if (!string.Equals(instance.CurrentStep, normalized, StringComparison.Ordinal))
        {
            instance.CurrentStep = normalized;
            instance.UpdatedAt = DateTimeOffset.UtcNow;
            await repo.SaveChangesAsync(ct);
        }

        return (new SetCurrentStepResult(instance.Id, instance.CurrentStep), null);
    }
}

/// <summary>Resultado del avance de paso (contrato con el frontend: id + paso persistido).</summary>
public sealed record SetCurrentStepResult(Guid Id, string? CurrentStep);
