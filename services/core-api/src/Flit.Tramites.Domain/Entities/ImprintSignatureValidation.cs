namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Bitácora append-only de una validación OT de firma digital de impronta manual
/// (<c>tramites.imprint_signature_validations</c>, HU #12148).
/// </summary>
public sealed class ImprintSignatureValidation
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid VehicleSignatureImprintId { get; set; }
    public Guid ProcedureInstanceId { get; set; }

    /// <summary>Snapshot de placa al validar (Trim + Upper).</summary>
    public string Placa { get; set; } = string.Empty;

    public Guid ValidatedBy { get; set; }
    public DateTimeOffset ValidatedAt { get; set; }

    /// <summary><c>valid</c>, <c>invalid</c> o <c>not_found</c>.</summary>
    public string Result { get; set; } = string.Empty;

    public string? FailureReason { get; set; }
}
