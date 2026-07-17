using Flit.Modules.Quipux.Domain.Consola;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Microsoft.Extensions.Logging;

namespace Flit.Modules.Quipux.Application.UseCases.Cola;

/// <summary>
/// Re-encola una radicación <c>fallido</c> (HU #10774). Delega la transición guardada en el
/// repositorio de consola y, si prosperó, deja el rastro en la bitácora del trámite
/// (<c>reintento_manual</c>) para que la reaparición del trámite en la cola no sea un hueco
/// inexplicable en el timeline. El actor se registra SOLO en el log de aplicación: la bitácora nunca
/// persiste al actor (regla PII del repo, ver <c>QuipuxSubmissionAuditLog</c>).
/// </summary>
public sealed class ReintentarSubmissionHandler
{
    private readonly IQuipuxSubmissionConsoleRepository _repository;
    private readonly IQuipuxAuditLog _auditLog;
    private readonly ILogger<ReintentarSubmissionHandler> _logger;

    public ReintentarSubmissionHandler(
        IQuipuxSubmissionConsoleRepository repository,
        IQuipuxAuditLog auditLog,
        ILogger<ReintentarSubmissionHandler> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<QuipuxManualOpStatus> HandleAsync(
        ReintentarSubmissionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await _repository
            .RetryAsync(command.SubmissionId, command.TransitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        if (result.Status == QuipuxManualOpStatus.Ok && result.TenantId is { } tenantId)
        {
            // detail SANITIZADO: solo estado/intentos previos, jamás el actor.
            await _auditLog.WriteAsync(
                tenantId,
                command.SubmissionId,
                QuipuxStage.ReintentoManual,
                QuipuxOutcome.Ok,
                new { estadoPrevio = result.PreviousStatus, intentosPrevios = result.PreviousAttempts },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            QuipuxColaLogMessages.SubmissionReintentada(_logger, command.SubmissionId, command.ActorUserId);
        }

        return result.Status;
    }
}
