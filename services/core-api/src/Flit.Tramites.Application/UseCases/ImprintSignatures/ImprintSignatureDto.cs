namespace Flit.Tramites.Application.UseCases.ImprintSignatures;

/// <summary>Resumen de una fila de bitácora (sin PII extra más allá de email/rol del validador).</summary>
public sealed record ImprintSignatureValidationSummaryDto(
    Guid Id,
    string Result,
    string? FailureReason,
    DateTimeOffset ValidatedAt,
    Guid ValidatedBy,
    string Placa,
    string? ValidatedByEmail = null,
    string? ValidatedByRole = null);

/// <summary>DTO de listado OT — nunca expone <c>private_key</c>.</summary>
public sealed record ImprintSignatureDto(
    Guid Id,
    Guid TenantId,
    Guid ProcedureInstanceId,
    string Placa,
    string ModuleCode,
    Guid? AttachmentId,
    string PublicKey,
    string DocumentHash,
    string Signature,
    DateTimeOffset SignedAt,
    bool WasSignedWithoutOwnerSignature,
    string? SignedStoragePath,
    string? SignedSha256,
    long? SignedSizeBytes,
    string? SignedFilename,
    DateTimeOffset? DeletedAt,
    ImprintSignatureValidationSummaryDto? LastValidation = null);
