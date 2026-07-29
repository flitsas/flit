using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Persons;

/// <summary>
/// Detalle de UNA validación de identidad por id, tenant-scoped — CF-06 (Feature #11004, ADR-0036).
/// Sirve tanto a prevalidaciones standalone (sin <c>instanceId</c>) como a validaciones de trámite:
/// reutiliza el mismo repositorio y el mismo mapeo (<see cref="IniciarBiometriaHandler.ToDto"/>) que ya
/// usa el listado por-instancia, para no duplicar la proyección a <see cref="BiometricValidationDto"/>.
/// Pensado para poll de detalle (patrón <c>KyverumPendingView</c> en FE): el consumidor llama este
/// endpoint cada pocos segundos mientras el estado no sea terminal.
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

        return (IniciarBiometriaHandler.ToDto(validation, DateTimeOffset.UtcNow), null);
    }
}
