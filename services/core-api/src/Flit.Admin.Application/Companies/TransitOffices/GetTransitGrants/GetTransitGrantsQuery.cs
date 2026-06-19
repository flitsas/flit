namespace Flit.Admin.Application.Companies.TransitOffices.GetTransitGrants;

/// <summary>Consulta de los grants habilitados de un tenant (AC5).</summary>
public sealed class GetTransitGrantsQuery
{
    public required Guid TenantId { get; init; }
}
