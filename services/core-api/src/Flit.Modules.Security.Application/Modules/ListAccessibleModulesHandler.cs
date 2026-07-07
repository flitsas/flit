using Flit.Modules.Security.Domain.Modules;

namespace Flit.Modules.Security.Application.Modules;

public sealed class ListAccessibleModulesHandler(ISecurityModuleRepository repository)
{
    public async Task<IReadOnlyList<AccessibleModuleDto>> HandleAsync(
        IReadOnlyList<string> callerPermissionSlugs,
        bool isSuperAdmin,
        Guid? tenantId,
        CancellationToken ct,
        string? targetEntityType = null)
    {
        return await repository.ListAccessibleAsync(callerPermissionSlugs, isSuperAdmin, tenantId, targetEntityType, ct);
    }
}
