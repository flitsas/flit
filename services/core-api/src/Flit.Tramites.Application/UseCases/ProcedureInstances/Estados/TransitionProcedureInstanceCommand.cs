using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;

/// <summary>
/// Handler del endpoint <c>POST /instances/{id}/transition</c> (N 03): traduce la orden HTTP a
/// <see cref="ITramiteLifecycleService"/> y proyecta el resultado al resumen del contrato.
/// El endpoint mapea <c>ErrorCode</c>/<c>ErrorDetail</c> a ProblemDetails (title = código).
/// <para>ADR-0036 §D9 (HU #10916): tras aprobar con un mandatario resuelto, regenera el mandato (y el
/// resto del expediente) con el firmante — best-effort, fuera de la unidad de trabajo de la transición.</para>
/// </summary>
public sealed class TransitionProcedureInstanceHandler(
    ITramiteLifecycleService lifecycle,
    GenerarFurHandler? furHandler = null,
    ILogger<TransitionProcedureInstanceHandler>? logger = null)
{
    public async Task<(ProcedureInstanceSummary? Result, string? ErrorCode, string? ErrorDetail)> HandleAsync(
        Guid id,
        Guid tenantId,
        string? toStatus,
        string? reason,
        Guid? changedByUserId,
        Guid? mandateSignerId = null,
        CancellationToken ct = default)
    {
        var status = toStatus?.Trim().ToLowerInvariant() ?? string.Empty;
        var outcome = await lifecycle.TransitionAsync(
            new TramiteTransitionCommand(id, tenantId, status, reason, changedByUserId, MandateSignerId: mandateSignerId),
            ct).ConfigureAwait(false);

        if (!outcome.Success)
            return (null, outcome.ErrorCode, outcome.ErrorDetail);

        // ADR-0036 §D9 (HU #10916) — al aprobar con firmante resuelto, regenerar el mandato con el
        // mandatario (el generado en preparado llevaba placeholders). Best-effort: un fallo aquí NO
        // revierte la aprobación ya persistida; el mandato puede regenerarse luego desde el FUR.
        if (furHandler is not null
            && status == TramiteEstado.Aprobado
            && outcome.Instance!.MandateSignerId is not null)
        {
            var (_, regenError) = await furHandler.HandleAsync(id, tenantId, ct).ConfigureAwait(false);
            if (regenError is not null && logger is not null)
                TransitionLog.RegeneracionMandatoOmitida(logger, id, regenError);
        }

        return (CreateProcedureInstanceHandler.ToSummary(outcome.Instance!), null, null);
    }
}

/// <summary>Logging source-generated (CA1848) de la transición. Sin PII.</summary>
internal static partial class TransitionLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo regenerar el mandato al aprobar (instancia {InstanceId}): {Reason}. Se conserva el mandato previo.")]
    public static partial void RegeneracionMandatoOmitida(ILogger logger, Guid instanceId, string reason);
}
