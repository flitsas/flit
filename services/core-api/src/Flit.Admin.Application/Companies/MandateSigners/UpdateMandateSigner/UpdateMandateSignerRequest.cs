namespace Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;

/// <summary>Cuerpo HTTP de edición de mandatario. La huella se regenera en el servidor.</summary>
public sealed record UpdateMandateSignerRequest(
    string? FullName,
    string? DocumentNumber,
    IReadOnlyList<Guid>? CompanyTenantIds,
    string? DocumentType = null,
    string? Email = null,
    Guid? UserId = null,
    /// <summary>
    /// HU #11201 — conjunto deseado de organismos. Ausente ⇒ no se tocan (la consola del organismo solo
    /// edita datos personales y compañías). Presente ⇒ reemplaza: los que no vengan se retiran.
    /// </summary>
    IReadOnlyList<Guid>? TransitOfficeIds = null,
    /// <summary>
    /// HU #13129 — modelo, forma de firma y vigencia. Ausentes ⇒ se conserva lo guardado (solo dentro de
    /// Persona natural). Persona jurídica y Formato en blanco no admiten forma de firma, fechas ni correo.
    /// </summary>
    string? SignerModel = null,
    string? SignatureMethod = null,
    string? ValidityKind = null,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null,
    /// <summary>
    /// HU #13179 — compañías asociadas por organismo. Ausente ⇒ no se tocan; cada organismo presente
    /// reemplaza su conjunto (lista vacía las retira).
    /// </summary>
    IReadOnlyList<Flit.Admin.Domain.Companies.MandateSigners.MandateSignerOfficeCompanies>? OfficeCompanies = null);
