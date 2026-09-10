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
    IReadOnlyList<Guid>? TransitOfficeIds = null);
