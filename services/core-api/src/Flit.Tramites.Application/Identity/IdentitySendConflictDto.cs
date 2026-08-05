namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Cuerpo informativo del 409 cuando la precedencia de envío impide crear/enviar (HU #11264, CF-03).
/// No incluye PII biométrica; solo metadatos de la validación/cobertura existente.
/// </summary>
public sealed record IdentitySendConflictDto(
    string Motivo,
    string? Status,
    DateTimeOffset? ValidatedAt,
    DateTimeOffset? ValidUntil,
    Guid? ValidationId,
    string? Origen)
{
    public static IdentitySendConflictDto From(IdentitySendDecision decision) =>
        new(
            decision.Motivo,
            decision.Status,
            decision.ValidatedAt,
            decision.ValidUntil,
            decision.ValidationId,
            decision.Origen);
}
