using Flit.Modules.Security.Domain.Modules;

namespace Flit.Modules.Security.Application.Modules;

public sealed class CreateModuleHandler(ISecurityModuleRepository repository)
{
    public async Task<Guid> HandleAsync(CreateModuleCommand command, CancellationToken ct)
    {
        var codeExists = await repository.CodeExistsAsync(command.Code, excludeId: null, ct);
        if (codeExists)
            throw new ModuleCodeDuplicateException();

        var id = await repository.CreateAsync(
            new SecurityModuleData(command.Code, command.Name, command.Description, command.SortOrder),
            ct);

        return id;
    }
}
