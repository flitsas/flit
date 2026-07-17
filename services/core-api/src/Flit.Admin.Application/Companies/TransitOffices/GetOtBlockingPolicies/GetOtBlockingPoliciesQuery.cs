namespace Flit.Admin.Application.Companies.TransitOffices.GetOtBlockingPolicies;

/// <summary>Consulta de las políticas de bloqueo por OT configuradas para un tenant (FEATURE 05).</summary>
public sealed class GetOtBlockingPoliciesQuery
{
    public required Guid TenantId { get; init; }
}
