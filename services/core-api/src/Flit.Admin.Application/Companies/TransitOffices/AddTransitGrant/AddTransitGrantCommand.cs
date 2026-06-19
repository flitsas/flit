namespace Flit.Admin.Application.Companies.TransitOffices.AddTransitGrant;

/// <summary>Comando para habilitar un organismo de tránsito a un tenant (AC2).</summary>
public sealed class AddTransitGrantCommand
{
    public required Guid TenantId { get; init; }

    public required Guid TransitOfficeId { get; init; }

    /// <summary>Id del usuario (claim <c>sub</c> del JWT) que habilita. Opcional.</summary>
    public Guid? CreatedBy { get; init; }

    /// <summary>Id de correlación opcional para trazabilidad de la auditoría.</summary>
    public Guid? CorrelationId { get; init; }
}
