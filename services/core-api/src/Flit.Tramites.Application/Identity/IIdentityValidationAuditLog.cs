namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Una entrada de la bitácora del ciclo de validación de identidad. Campos opcionales: cada paso rellena lo
/// que aplica. NUNCA incluir el secreto ni PII cruda (OCR) — solo ids/estados/flags.
/// </summary>
public sealed record IdentityValidationAuditEntry(
    string Stage,
    string Outcome,
    Guid? TenantId = null,
    Guid? ProcedureInstanceId = null,
    Guid? ValidationId = null,
    string? KyverumVerificationId = null,
    string? PartyRole = null,
    int? HttpStatus = null,
    bool? SignaturePresent = null,
    bool? SecretPresent = null,
    bool? DecryptOk = null,
    string? ProviderStatus = null,
    string? ErrorType = null,
    string? Message = null,
    string? Detail = null);

/// <summary>
/// Escribe la bitácora ÚNICA del ciclo de validación de identidad (envío, webhook, descifrado, errores,
/// reconciliación). La implementación persiste cada entrada de forma INDEPENDIENTE del flujo llamador (para
/// que quede registrada aunque el webhook termine en 500/401) y NUNCA lanza (la auditoría no rompe el flujo).
/// </summary>
public interface IIdentityValidationAuditLog
{
    Task LogAsync(IdentityValidationAuditEntry entry, CancellationToken ct = default);
}
