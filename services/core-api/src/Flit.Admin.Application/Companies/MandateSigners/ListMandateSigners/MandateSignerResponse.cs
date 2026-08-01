namespace Flit.Admin.Application.Companies.MandateSigners.ListMandateSigners;

/// <summary>
/// Vista de un mandatario para la gestión OT. <c>DocumentNumber</c> y <c>Email</c> son PII: se
/// entregan solo en esta respuesta autenticada de gestión (nunca en logs ni errores) para precargar
/// el formulario y mostrar el estado de la validación de identidad (HU #10993/#10994).
/// </summary>
public sealed record MandateSignerResponse(
    Guid Id,
    Guid TransitOfficeId,
    string FullName,
    string DocumentType,
    string DocumentNumber,
    string IntegrityHash,
    string? Email,
    Guid? UserId,
    Guid? IdentityValidationRef,
    Guid? SignatureVaultId,
    string IdentityStatus,
    DateTimeOffset RegisteredAt,
    bool IsActive,
    IReadOnlyList<Guid> CompanyTenantIds,
    /// <summary>
    /// HU #11201 — organismos donde aplica el mandatario. <c>TransitOfficeId</c> es solo el primario
    /// (deprecado): esta lista es la que dice dónde puede firmar.
    /// </summary>
    IReadOnlyList<Guid> TransitOfficeIds);
