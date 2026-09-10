using Flit.Admin.Domain.Companies.MandateSigners;
namespace Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;

/// <summary>Alta de un mandatario en un OT. <c>DocumentNumber</c> es PII: no loguear.</summary>
public sealed class CreateMandateSignerCommand
{
    public required Guid TransitOfficeId { get; init; }
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
    /// HU #11201 — organismos donde aplica. Vacío o nulo ⇒ solo <see cref="TransitOfficeId"/>.
    /// </summary>
    public IReadOnlyList<Guid>? TransitOfficeIds { get; init; }

    /// <summary>
    /// Organismos (subconjunto de los anteriores) en los que este mandatario firma A MANO: el contrato
    /// deja la línea de guiones bajos con sus datos debajo y no estampa firma del baúl ni sello de
    /// identidad. Va por organismo y no por persona porque la misma puede firmar a mano ante uno y
    /// electrónicamente ante otro.
    /// </summary>
    public IReadOnlyList<Guid>? PhysicalSignatureOfficeIds { get; init; }

    /// <summary>
    /// Firma del baúl elegida para el mandatario. <c>null</c> ⇒ el trámite la resuelve por documento,
    /// que es el comportamiento previo.
    /// </summary>
    public Guid? SignatureVaultId { get; init; }

    /// <summary>
    /// Empresas representadas para las que firma, POR ORGANISMO. Vacío o ausente ⇒ el mandatario aplica
    /// a todas las empresas de ese organismo, que es como se comportan los que ya existen.
    /// </summary>
    public IReadOnlyList<MandateSignerOfficeCompanies>? OfficeCompanies { get; init; }

    public Guid? CreatedBy { get; init; }
    public Guid? CorrelationId { get; init; }
}
