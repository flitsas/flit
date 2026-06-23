using Flit.Modules.Security.Domain.Modules;

namespace Flit.Modules.Security.Application.Modules;

public sealed class DeactivateModuleHandler(ISecurityModuleRepository repository)
{
    public async Task HandleAsync(Guid moduleId, CancellationToken ct)
    {
        var module = await repository.GetByIdAsync(moduleId, ct);
        if (module is null)
            throw new ModuleNotFoundException();

        await repository.DeactivateAsync(moduleId, ct);
    }
}
