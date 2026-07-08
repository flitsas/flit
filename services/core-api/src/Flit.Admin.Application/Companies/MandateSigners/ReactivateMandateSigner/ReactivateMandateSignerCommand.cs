namespace Flit.Admin.Application.Companies.MandateSigners.ReactivateMandateSigner;

/// <summary>Reactivación de un mandatario inactivado (vuelve activo, sin compañías).</summary>
public sealed class ReactivateMandateSignerCommand
{
    public required Guid TransitOfficeId { get; init; }
    public required Guid MandateSignerId { get; init; }
    public Guid? ChangedBy { get; init; }
    public Guid? CorrelationId { get; init; }
}
