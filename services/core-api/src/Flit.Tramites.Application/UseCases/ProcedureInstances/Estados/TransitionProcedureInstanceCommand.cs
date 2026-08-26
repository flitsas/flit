using Flit.Tramites.Domain.Repositories;
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
    ILogger<TransitionProcedureInstanceHandler>? logger = null,
    IDeferredSignatureMarkRepository? marks = null)
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

        // Al entregar: expediente con mandante/mandatario en blanco.
        // Al aprobar: siempre regenerar el mandato (abierto/institucional también llenan al mandante).
        if (furHandler is not null
            && (status == TramiteEstado.Entregado || status == TramiteEstado.Aprobado))
        {
            var (_, regenError) = await furHandler.HandleAsync(id, tenantId, ct).ConfigureAwait(false);
            if (regenError is not null && logger is not null)
                TransitionLog.RegeneracionMandatoOmitida(logger, id, regenError);

            await CerrarFirmaPosteriorDelMandatarioAsync(id, tenantId, regenError is null, ct)
                .ConfigureAwait(false);
        }

        return (CreateProcedureInstanceHandler.ToSummary(outcome.Instance!), null, null);
    }

    /// <summary>
    /// Cierra la marca de firma a posteriori del MANDATARIO cuando el expediente acaba de regenerarse.
    ///
    /// <para>Es aquí donde esa marca se resuelve, y no en el lote por validación de identidad: el
    /// mandatario valida su identidad en Admin, que no emite el evento que dispara el lote, y además puede
    /// resolverse capturando una firma del baúl, que no emite ningún evento en absoluto. La regeneración
    /// al entregar/aprobar ya recoge lo que exista en ese momento, así que la marca se cierra contra el
    /// hecho consumado en vez de contra una notificación que puede no llegar nunca.</para>
    ///
    /// <para>Best-effort: si la regeneración falló, la marca se queda pendiente —el mandato no se rehízo,
    /// así que darla por aplicada mentiría—. Un fallo al cerrarla no revierte la transición.</para>
    /// </summary>
    private async Task CerrarFirmaPosteriorDelMandatarioAsync(
        Guid id, Guid tenantId, bool regeneracionOk, CancellationToken ct)
    {
        if (marks is null || !regeneracionOk)
            return;

        try
        {
            var mark = await marks
                .FindPendienteAsync(tenantId, id, MarcarFirmaPosteriorHandler.ParteMandatario, ct)
                .ConfigureAwait(false);
            if (mark is null)
                return;

            mark.Aplicar(validationId: null, DateTimeOffset.UtcNow);
            await marks.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (logger is not null)
        {
            TransitionLog.RegeneracionMandatoOmitida(logger, id, ex.Message);
        }
    }
}

/// <summary>Logging source-generated (CA1848) de la transición. Sin PII.</summary>
internal static partial class TransitionLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo regenerar el mandato al aprobar (instancia {InstanceId}): {Reason}. Se conserva el mandato previo.")]
    public static partial void RegeneracionMandatoOmitida(ILogger logger, Guid instanceId, string reason);
}
