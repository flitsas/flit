using Flit.Admin.Domain.Companies.MandateSigners;
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
    string? DocumentNumber,
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
    IReadOnlyList<Guid> TransitOfficeIds,
    /// <summary>
    /// Subconjunto de <see cref="TransitOfficeIds"/> donde el mandatario firma A MANO. Lo necesita el
    /// formulario para precargar la marca al editar.
    /// </summary>
    IReadOnlyList<Guid>? PhysicalSignatureOfficeIds = null,
    /// <summary>
    /// HU #13179 — compañías asociadas (por tenant) por organismo. Lo necesita el formulario para precargar la
    /// selección al editar; vacío para un organismo significa «solo su propia compañía».
    /// </summary>
    IReadOnlyList<MandateSignerOfficeCompanies>? OfficeCompanies = null,
    /// <summary>HU #13129 — modelo: <c>natural</c> | <c>juridica</c> | <c>formato_blanco</c>.</summary>
    string SignerModel = MandateSignerModels.Natural,
    /// <summary>HU #13129 — forma de firma (<c>baul</c> | <c>biometria</c>); nula fuera de natural y en legados.</summary>
    string? SignatureMethod = null,
    /// <summary>HU #13129 — vigencia propia: <c>fixed</c> | <c>range</c>.</summary>
    string ValidityKind = MandateValidityKinds.Fixed,
    /// <summary>HU #13129 — inicio del rango (date); nulo con vigencia fija.</summary>
    DateOnly? ValidFrom = null,
    /// <summary>HU #13129 — fin del rango (date); nulo con vigencia fija.</summary>
    DateOnly? ValidTo = null,
    /// <summary>
    /// HU #13129 — estado de vigencia calculado en servidor (día de Colombia): <c>inactivo</c> →
    /// <c>vencido</c> → <c>por_vencer</c> (≤ 7 días al fin) → <c>vigente</c>; <c>no_vigente</c> si el rango
    /// aún no empieza. No se persiste.
    /// </summary>
    string ValidityStatus = MandateValidityStatus.Vigente,
    /// <summary>
    /// HU #13130 — firma válida: vigencia propia activa Y, con biometría, validación biométrica aprobada
    /// (sin renovación, HU #13130b). Nulo si el modelo no es Persona natural.
    /// </summary>
    bool? SignatureValid = null,
    /// <summary>
    /// HU #13130 — motivo cuando <c>SignatureValid</c> es falso: <c>mandatario_fuera_de_vigencia</c>,
    /// <c>mandatario_inactivo</c> o <c>sin_validacion_aprobada</c>.
    /// </summary>
    string? SignatureInvalidReason = null);
