namespace Flit.Admin.Application.Companies.TransitOffices.RemoveTransitGrant;

/// <summary>Comando para deshabilitar un grant de organismo de tránsito (AC3).</summary>
public sealed class RemoveTransitGrantCommand
{
    public required Guid TenantId { get; init; }

    public required Guid TransitOfficeId { get; init; }

    /// <summary>Id del usuario (claim <c>sub</c> del JWT) que deshabilita. Opcional.</summary>
    public Guid? ChangedBy { get; init; }

    /// <summary>Id de correlación opcional para trazabilidad de la auditoría.</summary>
    public Guid? CorrelationId { get; init; }
}
