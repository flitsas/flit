using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.Identity;

public enum IdentitySignatureCaptureOutcome
{
    Captured,
    AlreadyPresent,
    Skipped,
    Retryable,
}

/// <summary>
/// Orquesta descarga (si hace falta), recorte y persistencia de la rúbrica. Idempotente y best-effort:
/// no lanza por fallos de Kyverum ni de layout (salvo cancelación).
/// </summary>
public interface IIdentitySignatureCapture
{
    Task<IdentitySignatureCaptureOutcome> EnsureAsync(
        ProcedureInstanceBiometricValidation validation,
        CancellationToken cancellationToken = default);

    Task<IdentitySignatureCaptureOutcome> EnsureFromPdfAsync(
        ProcedureInstanceBiometricValidation validation,
        byte[] pdfBytes,
        CancellationToken cancellationToken = default);

    /// <summary>Carga la validación, captura y persiste si hubo recorte nuevo.</summary>
    Task<IdentitySignatureCaptureOutcome> EnsureForValidationAsync(
        Guid validationId,
        CancellationToken cancellationToken = default);
}
