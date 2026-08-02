namespace Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;

/// <summary>
/// Edición de un mandatario (RF23). Regenera la huella de integridad con la fecha de registro
/// original. <c>DocumentNumber</c> es PII: no loguear.
/// </summary>
public sealed class UpdateMandateSignerCommand
{
    /// <summary>
    /// Organismo bajo el que se edita: se valida su operabilidad y su tenant firma la auditoría. Tras la
    /// edición queda como PRIMARIO del mandatario.
    /// </summary>
    public required Guid TransitOfficeId { get; init; }

    /// <summary>
    /// Organismo primario que el mandatario tiene AHORA en base de datos, cuando difiere del anterior.
    ///
    /// <para>Existe porque la edición desde el configurador de la compañía (HU #11202) no se hace "bajo
    /// un organismo": el gestor manda la lista completa de organismos donde aplica. Usar el primero de
    /// esa lista como identidad hacía que la edición respondiera 404 en cuanto ese primero no coincidía
    /// con el primario guardado —por ejemplo al añadir un organismo nuevo—. La identidad se comprueba
    /// contra ESTE valor; <c>null</c> ⇒ contra <see cref="TransitOfficeId"/>, que es el comportamiento de
    /// la edición desde el perfil del organismo.</para>
    /// </summary>
    public Guid? OrganismoPrimarioActual { get; init; }
    public required Guid MandateSignerId { get; init; }
    public required string FullName { get; init; }
    public required string DocumentNumber { get; init; }
    public required IReadOnlyList<Guid> CompanyTenantIds { get; init; }

    /// <summary>Tipo de documento (ADR-0036); por defecto CC.</summary>
    public string DocumentType { get; init; } = "CC";

    /// <summary>Correo para la validación de identidad (ADR-0036, HU #10911). PII.</summary>
    public string? Email { get; init; }

    /// <summary>Cuenta de usuario de OT del mandatario (ADR-0036 §D9).</summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// HU #11201 — conjunto deseado de organismos. <c>null</c> ⇒ no se tocan; una lista los reemplaza.
    /// </summary>
    public IReadOnlyList<Guid>? TransitOfficeIds { get; init; }

    public Guid? UpdatedBy { get; init; }
    public Guid? CorrelationId { get; init; }
}
