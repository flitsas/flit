namespace Flit.Admin.Application.Companies.Settings.GetTenantSettings;

/// <summary>Petición de la configuración operativa actual de un tenant (AC3).</summary>
public sealed class GetTenantSettingsQuery
{
    public required Guid TenantId { get; init; }
}
