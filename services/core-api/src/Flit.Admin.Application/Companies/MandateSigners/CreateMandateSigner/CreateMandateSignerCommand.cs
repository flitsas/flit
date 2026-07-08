namespace Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;

/// <summary>Alta de un mandatario en un OT. <c>DocumentNumber</c> es PII: no loguear.</summary>
public sealed class CreateMandateSignerCommand
{
    public required Guid TransitOfficeId { get; init; }
    public required string FullName { get; init; }
    public required string DocumentNumber { get; init; }
    public required IReadOnlyList<Guid> CompanyTenantIds { get; init; }
    public Guid? CreatedBy { get; init; }
    public Guid? CorrelationId { get; init; }
}
