namespace Flit.Admin.Application.Companies.MandateSigners.InactivateMandateSigner;

/// <summary>Inactivación (baja lógica) de un mandatario (RF24): libera sus compañías.</summary>
public sealed class InactivateMandateSignerCommand
{
    public required Guid TransitOfficeId { get; init; }
    public required Guid MandateSignerId { get; init; }
    public Guid? ChangedBy { get; init; }
    public Guid? CorrelationId { get; init; }
}
