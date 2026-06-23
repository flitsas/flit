using Flit.Modules.Security.Domain.Permissions;

namespace Flit.Modules.Security.Application.Permissions;

public sealed class ListPermissionsHandler(IPermissionRepository repository)
{
    public async Task<IReadOnlyList<PermissionSummary>> HandleAsync(Guid? moduleId, CancellationToken ct)
    {
        return await repository.ListByModuleAsync(moduleId, ct);
    }
}
