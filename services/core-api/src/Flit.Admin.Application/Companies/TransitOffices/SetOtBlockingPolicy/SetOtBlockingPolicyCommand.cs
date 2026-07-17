namespace Flit.Admin.Application.Companies.TransitOffices.SetOtBlockingPolicy;

/// <summary>Comando para fijar el estado deseado de una política de bloqueo por OT (FEATURE 05).</summary>
public sealed class SetOtBlockingPolicyCommand
{
    public required Guid TenantId { get; init; }

    public required Guid TransitOfficeId { get; init; }

    public required string? Criterion { get; init; }

    public required bool Blocks { get; init; }

    /// <summary>Id del usuario (claim <c>sub</c> del JWT) que hace el cambio. Opcional.</summary>
    public Guid? ChangedBy { get; init; }

    /// <summary>Id de correlación opcional para trazabilidad de la auditoría.</summary>
    public Guid? CorrelationId { get; init; }
}
