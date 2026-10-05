namespace Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;

/// <summary>
/// Cuerpo HTTP de alta de mandatario. La huella se autogenera en el servidor. <c>DocumentType</c>,
/// <c>Email</c> y <c>UserId</c> son ADR-0036 (opcionales): correo para la validación de identidad y
/// cuenta de usuario para el cotejo del firmante al aprobar.
/// </summary>
public sealed record CreateMandateSignerRequest(
    string? FullName,
    string? DocumentNumber,
    IReadOnlyList<Guid>? CompanyTenantIds,
    string? DocumentType = null,
    string? Email = null,
    Guid? UserId = null,
    /// <summary>
    /// HU #11201 — organismos donde aplica el mandatario. Ausente ⇒ solo el organismo de la ruta, que
    /// es como da de alta la consola del propio organismo.
    /// </summary>
    IReadOnlyList<Guid>? TransitOfficeIds = null,
    /// <summary>
    /// HU #13123 — firma del baúl de la compañía elegida para el mandatario. El OT solo envía el id: no
    /// recibe la lista del baúl. Se valida en backend contra el tenant de la compañía.
    /// </summary>
    Guid? SignatureVaultId = null,
    /// <summary>HU #13129 — <c>natural</c> (por defecto) | <c>juridica</c> | <c>formato_blanco</c>.</summary>
    string? SignerModel = null,
    /// <summary>HU #13129 — forma de firma de la Persona natural: <c>baul</c> | <c>biometria</c>.</summary>
    string? SignatureMethod = null,
    /// <summary>HU #13129 — <c>fixed</c> (por defecto) | <c>range</c>.</summary>
    string? ValidityKind = null,
    /// <summary>HU #13129 — inicio del rango (date, <c>yyyy-MM-dd</c>); solo con <c>range</c>.</summary>
    DateOnly? ValidFrom = null,
    /// <summary>HU #13129 — fin del rango (date); solo con <c>range</c>.</summary>
    DateOnly? ValidTo = null,
    /// <summary>
    /// HU #13179 — compañías de FLIT (por tenant) a las que se asocia el mandatario, por organismo. Ausente o
    /// vacío ⇒ solo aplica a su propia compañía. El OT solo puede asociar compañías que operan en su organismo.
    /// </summary>
    IReadOnlyList<Flit.Admin.Domain.Companies.MandateSigners.MandateSignerOfficeCompanies>? OfficeCompanies = null);
