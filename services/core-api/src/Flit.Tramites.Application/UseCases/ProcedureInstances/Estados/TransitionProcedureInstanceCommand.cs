using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;

/// <summary>
/// Handler del endpoint <c>POST /instances/{id}/transition</c> (N 03): traduce la orden HTTP a
/// <see cref="ITramiteLifecycleService"/> y proyecta el resultado al resumen del contrato.
/// El endpoint mapea <c>ErrorCode</c>/<c>ErrorDetail</c> a ProblemDetails (title = código).
/// </summary>
public sealed class TransitionProcedureInstanceHandler(ITramiteLifecycleService lifecycle)
{
    public async Task<(ProcedureInstanceSummary? Result, string? ErrorCode, string? ErrorDetail)> HandleAsync(
        Guid id,
        Guid tenantId,
        string? toStatus,
        string? reason,
        Guid? changedByUserId,
        CancellationToken ct = default)
    {
        var outcome = await lifecycle.TransitionAsync(
            new TramiteTransitionCommand(id, tenantId, toStatus?.Trim().ToLowerInvariant() ?? string.Empty, reason, changedByUserId),
            ct).ConfigureAwait(false);

        return outcome.Success
            ? (CreateProcedureInstanceHandler.ToSummary(outcome.Instance!), null, null)
            : (null, outcome.ErrorCode, outcome.ErrorDetail);
    }
}
