using Flit.Modules.Security.Domain.Modules;

namespace Flit.Modules.Security.Application.Modules;

public sealed class DeleteModuleHandler(ISecurityModuleRepository repository)
{
    public async Task HandleAsync(Guid moduleId, CancellationToken ct)
    {
        var module = await repository.GetByIdAsync(moduleId, ct);
        if (module is null)
            throw new ModuleNotFoundException();

        var hasPermissions = await repository.HasActivePermissionsAsync(moduleId, ct);
        if (hasPermissions)
            throw new ModuleHasActivePermissionsException();

        await repository.SoftDeleteAsync(moduleId, ct);
    }
}
