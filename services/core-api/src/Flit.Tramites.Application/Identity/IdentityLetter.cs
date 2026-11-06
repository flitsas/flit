namespace Flit.Tramites.Application.Identity;

/// <summary>
/// HU #14301 (Feature #14300, Épica #14271) — datos de la carta de identidad que FLIT emite al aprobar una validación MANUAL.
/// Sin QR, score, proveedor externo, fecha de nacimiento, sexo ni el usuario que revisó. Los instantes viajan crudos: el generador
/// los formatea en hora de Colombia con el formato del producto.
/// </summary>
public sealed record IdentityLetterData(
    string Radicado,
    string CompanyName,
    string Reference,
    DateTimeOffset IssuedAt,
    string RoleLabel,
    string RegisteredName,
    string DocumentType,
    string DocumentNumber,
    DateTimeOffset ValidatedAt,
    DateTimeOffset? ValidUntil,
    string Serie,
    string EvidenceSha256,
    DateTimeOffset SealedAt,
    string? ConsentIp,
    DateTimeOffset? ConsentAt,
    string? ConsentTextVersion,
    ManualIdentityAuthorizationTexts.Texts? Texts,
    byte[] Selfie,
    byte[] IdFront,
    byte[] IdBack,
    byte[] Signature);

/// <summary>Genera el PDF de la carta. Implementación en Infrastructure (QuestPDF).</summary>
public interface IIdentityLetterPdfGenerator
{
    byte[] Generate(IdentityLetterData data);
}

public enum IdentityLetterOutcome
{
    /// <summary>Se generó y se guardó la carta en esta llamada.</summary>
    Generated,

    /// <summary>La validación ya tenía carta: no se tocó nada (idempotente).</summary>
    AlreadyPresent,

    /// <summary>No le corresponde carta de FLIT (no es una aprobación manual) o la validación no existe.</summary>
    Skipped,
}

/// <summary>La carta no pudo generarse (imagen ausente, storage caído...). Se reintenta: no revierte la aprobación.</summary>
public sealed class IdentityLetterGenerationException : Exception
{
    public IdentityLetterGenerationException(string message) : base(message) { }

    public IdentityLetterGenerationException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Genera, UNA sola vez, la carta de identidad de una validación manual aprobada. La aprobación NO depende de este paso: corre
/// después, desde la outbox del evento de completado, y si falla lanza <see cref="IdentityLetterGenerationException"/> para que la
/// outbox reintente.
/// </summary>
public interface IIdentityLetterService
{
    Task<IdentityLetterOutcome> EnsureForValidationAsync(Guid validationId, CancellationToken ct = default);
}
