using Flit.Modules.Security.Domain.Modules;

namespace Flit.Modules.Security.Application.Modules;

public sealed class UpdateModuleHandler(ISecurityModuleRepository repository)
{
    public async Task HandleAsync(UpdateModuleCommand command, CancellationToken ct)
    {
        var module = await repository.GetByIdAsync(command.Id, ct);
        if (module is null)
            throw new ModuleNotFoundException();

        await repository.UpdateAsync(
            command.Id,
            new UpdateModuleData(command.Name, command.Description, command.SortOrder),
            ct);
    }
}
