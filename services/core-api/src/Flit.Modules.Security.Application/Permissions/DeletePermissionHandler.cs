using Flit.Modules.Security.Domain.Permissions;

namespace Flit.Modules.Security.Application.Permissions;

public sealed class DeletePermissionHandler(IPermissionRepository repository)
{
    public async Task HandleAsync(Guid permissionId, CancellationToken ct)
    {
        var exists = await repository.ExistsAsync(permissionId, ct);
        if (!exists)
            throw new PermissionNotFoundException();

        var inUse = await repository.IsInUseByRolesAsync(permissionId, ct);
        if (inUse)
            throw new PermissionInUseException();

        await repository.SoftDeleteAsync(permissionId, ct);
    }
}
