using Flit.Modules.Quipux.Domain.Consola;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Microsoft.Extensions.Logging;

namespace Flit.Modules.Quipux.Application.UseCases.Cola;

/// <summary>
/// Cancela una radicación <c>pendiente</c> antes de que se radique (HU #10774): la lleva a
/// <c>fallido</c> terminal. Delega la transición guardada (con filtro por estado, para no pisar a un
/// worker que la reclamó entre la carga de la pantalla y el clic) en el repositorio de consola y, si
/// prosperó, escribe <c>cancelado_manual</c> en la bitácora del trámite. El actor va solo al log de
/// aplicación (regla PII del repo).
/// </summary>
public sealed class CancelarSubmissionHandler
{
    private readonly IQuipuxSubmissionConsoleRepository _repository;
    private readonly IQuipuxAuditLog _auditLog;
    private readonly ILogger<CancelarSubmissionHandler> _logger;

    public CancelarSubmissionHandler(
        IQuipuxSubmissionConsoleRepository repository,
        IQuipuxAuditLog auditLog,
        ILogger<CancelarSubmissionHandler> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<QuipuxManualOpStatus> HandleAsync(
        CancelarSubmissionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await _repository
            .CancelAsync(command.SubmissionId, command.TransitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        if (result.Status == QuipuxManualOpStatus.Ok && result.TenantId is { } tenantId)
        {
            // outcome = omitido: la radicación no se llegó a ejecutar, se descartó a propósito.
            await _auditLog.WriteAsync(
                tenantId,
                command.SubmissionId,
                QuipuxStage.CanceladoManual,
                QuipuxOutcome.Omitido,
                new { estadoPrevio = result.PreviousStatus },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            QuipuxColaLogMessages.SubmissionCancelada(_logger, command.SubmissionId, command.ActorUserId);
        }

        return result.Status;
    }
}
