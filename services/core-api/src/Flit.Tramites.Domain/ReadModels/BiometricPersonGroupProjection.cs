namespace Flit.Tramites.Domain.ReadModels;

/// <summary>
/// Proyección SQL/EF de una fila agrupada por persona (HU #11270): la validación más reciente
/// del documento normalizado + contador de validaciones del grupo.
/// </summary>
public sealed class BiometricPersonGroupProjection
{
    public Guid LatestValidationId { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public string DocumentNumber { get; init; } = string.Empty;
    public string DocumentTypeNorm { get; init; } = string.Empty;
    public string DocumentNumberNorm { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ValidatedAt { get; init; }
    public DateTimeOffset? ValidUntil { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public Guid? ProcedureInstanceId { get; init; }
    public string? ReferenceNumber { get; init; }
    public string? Modalidad { get; init; }
    public string? PartyRole { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public int? Score { get; init; }
    /// <summary>URL de captura de la validación más reciente (null si aún no hay enlace).</summary>
    public string? CaptureUrl { get; init; }
    public int ValidationCount { get; init; }
    /// <summary>Intentos consumidos y máximo permitido de la validación MÁS RECIENTE (HU #11505 AC3).</summary>
    public int Attempts { get; init; }
    public int MaxAttempts { get; init; }
}
