using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners.ReactivateMandateSigner;

/// <summary>Reactivación de un mandatario inactivado (vuelve activo y recupera sus vínculos).</summary>
public sealed class ReactivateMandateSignerCommand
{
    public required Guid TransitOfficeId { get; init; }
    public required Guid MandateSignerId { get; init; }
    public Guid? ChangedBy { get; init; }
    public Guid? CorrelationId { get; init; }

    /// <summary>HU #13138 — rol de quien reactiva, para la bitácora.</summary>
    public MandateSignerActorKind ActorKind { get; init; } = MandateSignerActorKind.None;
}
