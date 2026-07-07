using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #10536 — marca o desmarca un trámite como prioritario para que el OT lo revise con primacía.
/// Solo cambia el flag de ordenamiento (<c>procedure_instances.prioritario</c>): NO altera el estado
/// del ciclo de vida ni los gates (RF04), por eso está disponible en cualquier estado no eliminado.
/// Idempotente: si el flag ya tiene el valor pedido, no re-persiste.
///
/// Código de error: <c>not_found</c> (la instancia no existe o no pertenece al tenant).
/// </summary>
public sealed class SetPriorityProcedureInstanceHandler(IProcedureInstanceRepository repo)
{
    public async Task<(SetPriorityResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        bool prioritario,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        if (instance.Prioritario != prioritario)
        {
            instance.Prioritario = prioritario;
            instance.UpdatedAt = DateTimeOffset.UtcNow;
            await repo.SaveChangesAsync(ct);
        }

        return (new SetPriorityResult(instance.Id, instance.Prioritario), null);
    }
}

/// <summary>Resultado del marcado de prioridad (contrato con el frontend: id + estado del flag).</summary>
public sealed record SetPriorityResult(Guid Id, bool Prioritario);
