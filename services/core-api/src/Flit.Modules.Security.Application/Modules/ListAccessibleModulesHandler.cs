using Flit.Modules.Security.Domain.Modules;

namespace Flit.Modules.Security.Application.Modules;

public sealed class ListAccessibleModulesHandler(ISecurityModuleRepository repository)
{
    public async Task<IReadOnlyList<AccessibleModuleDto>> HandleAsync(
        IReadOnlyList<string> callerPermissionSlugs,
        bool isSuperAdmin,
        string? productCode,
        CancellationToken ct)
    {
        return await repository.ListAccessibleAsync(callerPermissionSlugs, isSuperAdmin, productCode, ct);
    }
}
