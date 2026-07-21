using Microsoft.Extensions.Logging;

namespace Flit.Modules.Quipux.Application.UseCases.Cola;

/// <summary>
/// Logging source-generated (CA1848) de las acciones manuales de la consola de cola Quipux. El
/// actor SÍ se registra aquí —el log de aplicación es su sitio— porque no puede ir en la bitácora
/// de <c>quipux_submission_events</c> (regla PII del repo).
/// </summary>
internal static partial class QuipuxColaLogMessages
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Radicación Quipux {SubmissionId} re-encolada manualmente por el actor {ActorUserId}.")]
    public static partial void SubmissionReintentada(ILogger logger, Guid submissionId, Guid? actorUserId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Radicación Quipux {SubmissionId} cancelada manualmente por el actor {ActorUserId}.")]
    public static partial void SubmissionCancelada(ILogger logger, Guid submissionId, Guid? actorUserId);
}
