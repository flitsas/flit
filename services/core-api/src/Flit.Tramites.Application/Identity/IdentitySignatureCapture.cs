using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Captura idempotente de la rúbrica Kyverum. No lanza por proveedor ni layout (ADR-0054).
/// </summary>
public sealed class IdentitySignatureCapture(
    IKyverumCertificateClient certClient,
    IIdentitySignatureExtractor extractor,
    IIdentitySignatureArtifactStorage artifacts,
    IProcedureInstanceRepository repo,
    ILogger<IdentitySignatureCapture> logger) : IIdentitySignatureCapture
{
    public async Task<IdentitySignatureCaptureOutcome> EnsureAsync(
        ProcedureInstanceBiometricValidation validation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validation);
        var gate = await GateAsync(validation, cancellationToken).ConfigureAwait(false);
        if (gate is not null)
            return gate.Value;

        try
        {
            var cert = await certClient
                .DownloadCertificateAsync(validation.KyverumVerificationId!, cancellationToken)
                .ConfigureAwait(false);
            if (cert is null || cert.Content.Length == 0)
                return IdentitySignatureCaptureOutcome.Retryable;

            return await PersistAsync(validation, cert.Content, cancellationToken).ConfigureAwait(false);
        }
        catch (KyverumCertificateException ex) when (ex.Transient)
        {
            IdentitySignatureCaptureLog.Retryable(logger, validation.Id);
            return IdentitySignatureCaptureOutcome.Retryable;
        }
        catch (KyverumCertificateException)
        {
            IdentitySignatureCaptureLog.Skipped(logger, validation.Id);
            return IdentitySignatureCaptureOutcome.Skipped;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            IdentitySignatureCaptureLog.Retryable(logger, validation.Id);
            return IdentitySignatureCaptureOutcome.Retryable;
        }
    }

    public async Task<IdentitySignatureCaptureOutcome> EnsureFromPdfAsync(
        ProcedureInstanceBiometricValidation validation,
        byte[] pdfBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(pdfBytes);
        var gate = await GateAsync(validation, cancellationToken).ConfigureAwait(false);
        if (gate is not null)
            return gate.Value;
        if (pdfBytes.Length == 0)
            return IdentitySignatureCaptureOutcome.Retryable;

        try
        {
            return await PersistAsync(validation, pdfBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            IdentitySignatureCaptureLog.Retryable(logger, validation.Id);
            return IdentitySignatureCaptureOutcome.Retryable;
        }
    }

    /// <summary>Carga la validación por id y captura. Usado por la outbox (mismo scope/DbContext).</summary>
    public async Task<IdentitySignatureCaptureOutcome> EnsureForValidationAsync(
        Guid validationId,
        CancellationToken cancellationToken = default)
    {
        var validation = await repo.GetBiometricByIdAsync(validationId, cancellationToken).ConfigureAwait(false);
        if (validation is null)
            return IdentitySignatureCaptureOutcome.Skipped;

        var outcome = await EnsureAsync(validation, cancellationToken).ConfigureAwait(false);
        if (outcome == IdentitySignatureCaptureOutcome.Captured)
            await repo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    private async Task<IdentitySignatureCaptureOutcome?> GateAsync(
        ProcedureInstanceBiometricValidation validation,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(validation.Provider, BiometricProviders.Kyverum, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(validation.KyverumVerificationId))
            return IdentitySignatureCaptureOutcome.Skipped;

        if (string.IsNullOrWhiteSpace(validation.SignatureImagePath))
            return null;

        if (await ArtifactLooksValidAsync(validation.SignatureImagePath, cancellationToken).ConfigureAwait(false))
            return IdentitySignatureCaptureOutcome.AlreadyPresent;

        return null;
    }

    private async Task<bool> ArtifactLooksValidAsync(string storagePath, CancellationToken cancellationToken)
    {
        try
        {
            var stream = await artifacts.OpenReadAsync(storagePath, cancellationToken).ConfigureAwait(false);
            if (stream is null)
                return false;
            await using (stream.ConfigureAwait(false))
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                var bytes = ms.ToArray();
                return IdentitySignatureImageFormat.IsSupported(bytes) && extractor.IsUsableInk(bytes);
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<IdentitySignatureCaptureOutcome> PersistAsync(
        ProcedureInstanceBiometricValidation validation,
        byte[] pdfBytes,
        CancellationToken cancellationToken)
    {
        var crop = extractor.TryExtract(pdfBytes);
        if (crop is null
            || !IdentitySignatureImageFormat.IsSupported(crop.PngBytes)
            || !extractor.IsUsableInk(crop.PngBytes))
        {
            IdentitySignatureCaptureLog.Skipped(logger, validation.Id);
            return IdentitySignatureCaptureOutcome.Skipped;
        }

        var stored = await artifacts.SaveAsync(validation.TenantId, crop.PngBytes, cancellationToken)
            .ConfigureAwait(false);
        validation.SignatureImagePath = stored.StoragePath;
        validation.SignatureImageSha256 = stored.Sha256;
        validation.UpdatedAt = DateTimeOffset.UtcNow;
        IdentitySignatureCaptureLog.Captured(logger, validation.Id);
        return IdentitySignatureCaptureOutcome.Captured;
    }
}

internal static partial class IdentitySignatureCaptureLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Rúbrica de identidad capturada para la validación {ValidationId}.")]
    public static partial void Captured(ILogger logger, Guid validationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rúbrica de identidad omitida para la validación {ValidationId} (sin imagen extraíble o proveedor no Kyverum).")]
    public static partial void Skipped(ILogger logger, Guid validationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rúbrica de identidad no disponible aún para la validación {ValidationId}; se reintentará.")]
    public static partial void Retryable(ILogger logger, Guid validationId);
}
