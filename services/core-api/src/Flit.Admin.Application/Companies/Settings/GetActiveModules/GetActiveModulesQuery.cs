namespace Flit.Admin.Application.Companies.Settings.GetActiveModules;

/// <summary>Petición de los 3 flags de módulos activos del dashboard de un tenant (HU #12251, Feature #12249).</summary>
public sealed class GetActiveModulesQuery
{
    public required Guid TenantId { get; init; }
}
