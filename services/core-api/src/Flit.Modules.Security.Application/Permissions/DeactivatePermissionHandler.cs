using Flit.Modules.Security.Domain.Permissions;

namespace Flit.Modules.Security.Application.Permissions;

public sealed class DeactivatePermissionHandler(IPermissionRepository repository)
{
    public async Task HandleAsync(Guid permissionId, CancellationToken ct)
    {
        var exists = await repository.ExistsAsync(permissionId, ct);
        if (!exists)
            throw new PermissionNotFoundException();

        await repository.DeactivateAsync(permissionId, ct);
    }
}
