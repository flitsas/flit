namespace Flit.Admin.Application.Companies.Whitelist.GetWhitelist;

/// <summary>Consulta de la lista blanca activa de un tenant (AC6).</summary>
public sealed class GetWhitelistQuery
{
    public required Guid TenantId { get; init; }
}
