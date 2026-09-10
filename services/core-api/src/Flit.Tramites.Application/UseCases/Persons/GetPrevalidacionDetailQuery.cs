using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Persons;

/// <summary>
/// Detalle de UNA validación de identidad por id, tenant-scoped — CF-06 (Feature #11004, ADR-0036).
/// Sirve tanto a prevalidaciones standalone (sin <c>instanceId</c>) como a validaciones de trámite:
/// reutiliza el mismo repositorio y el mismo mapeo (<see cref="IniciarBiometriaHandler.ToDto"/>) que ya
/// usa el listado por-instancia, para no duplicar la proyección a <see cref="BiometricValidationDto"/>.
/// Pensado para poll de detalle (patrón <c>KyverumPendingView</c> en FE): el consumidor llama este
/// endpoint cada pocos segundos mientras el estado no sea terminal.
/// HU #11069 — enriquece el DTO con el trámite primario y <c>linkedProcedures</c> (misma identidad).
/// </summary>
public sealed class GetPrevalidacionDetailHandler(IProcedureInstanceRepository repo)
{
    public async Task<(BiometricValidationDto? Result, string? Error)> HandleAsync(
        Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var validation = await repo.GetBiometricByIdAsync(id, ct);
        // "No encontrada" real: no existe o es de otro tenant. El tenant es la frontera dura (mismo
        // criterio que GetIdentityAuditHandler/GetIdentityAuditByValidationHandler).
        if (validation is null || validation.TenantId != tenantId)
            return (null, "not_found");

        var now = DateTimeOffset.UtcNow;
        var baseDto = IniciarBiometriaHandler.ToDto(validation, now);
        var linked = await LoadLinkedProceduresAsync(tenantId, validation, ct);

        return (baseDto with
        {
            ProcedureInstanceId = validation.ProcedureInstanceId,
            ReferenceNumber = validation.ProcedureInstance?.ReferenceNumber,
            Modalidad = (validation.ProcedureInstance != null && validation.ProcedureInstance.ProcedureType != null ? validation.ProcedureInstance.ProcedureType.Family : ""),
            LinkedProcedures = linked,
        }, null);
    }

    private async Task<IReadOnlyList<LinkedProcedureDto>> LoadLinkedProceduresAsync(
        Guid tenantId,
        ProcedureInstanceBiometricValidation validation,
        CancellationToken ct)
    {
        var summaries = await repo.ListLinkedProceduresByIdentityDocumentsAsync(
            tenantId,
            [(validation.DocumentType, validation.DocumentNumber)],
            ct);

        var identityKey = BiometricRules.IdentidadKey(
            tenantId, validation.DocumentType, validation.DocumentNumber);
        var all = summaries.GetValueOrDefault(identityKey) ?? [];

        // El primario va en ProcedureInstanceId/ReferenceNumber; aquí solo los demás.
        return all
            .Where(s => validation.ProcedureInstanceId is null
                || s.InstanceId != validation.ProcedureInstanceId.Value)
            .Select(s => new LinkedProcedureDto(s.InstanceId, s.ReferenceNumber, s.Status, s.Modalidad))
            .ToList();
    }
}
