namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Regla de tipo de mandato (signer | institutional | open) para el par compañía gestora × OT.
/// La plantilla del documento sigue en <see cref="TransitOfficeMandateConfigEntity"/>.
/// </summary>
public sealed class CompanyOtMandateRuleEntity
{
    public Guid Id { get; set; }
    public Guid CompanyTenantId { get; set; }
    public Guid TransitOfficeId { get; set; }

    /// <summary><c>signer</c> | <c>institutional</c> | <c>open</c>.</summary>
    public string AssignmentMode { get; set; } = "signer";

    public string MandataryFamily { get; set; } = "individuo";
    public string? InstitutionalMandataryName { get; set; }
    public string? InstitutionalMandataryNit { get; set; }
    public string? ChamberCity { get; set; }
    public string? MandatarySigla { get; set; }

    /// <summary>
    /// Mandatario persona preferido cuando <see cref="AssignmentMode"/> es <c>signer</c>.
    /// Preselección en el wizard; no aplica a institucional/abierto.
    /// </summary>
    public Guid? DefaultMandateSignerId { get; set; }

    /// <summary>
    /// HU #13128 (ADR-0061) — origen de la configuración: <c>organismo</c> | <c>compania</c> | <c>super_admin</c>.
    /// </summary>
    public string ConfiguredByScope { get; set; } = "organismo";

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
