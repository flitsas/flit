namespace Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;

/// <summary>
/// Edición de un mandatario (RF23). Regenera la huella de integridad con la fecha de registro
/// original. <c>DocumentNumber</c> es PII: no loguear.
/// </summary>
public sealed class UpdateMandateSignerCommand
{
    public required Guid TransitOfficeId { get; init; }
    public required Guid MandateSignerId { get; init; }
    public required string FullName { get; init; }
    public required string DocumentNumber { get; init; }
    public required IReadOnlyList<Guid> CompanyTenantIds { get; init; }
    public Guid? UpdatedBy { get; init; }
    public Guid? CorrelationId { get; init; }
}
