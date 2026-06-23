using Flit.Modules.Security.Domain.Roles;

namespace Flit.Modules.Security.Application.Roles;

public sealed class CreateRoleHandler(IRoleRepository repository)
{
    public async Task<Guid> HandleAsync(CreateRoleCommand command, CancellationToken ct)
    {
        var codeExists = await repository.CodeExistsInTenantAsync(command.TenantId, command.Code, ct);
        if (codeExists)
            throw new RoleCodeDuplicateException();

        var id = await repository.CreateAsync(
            new CreateRoleData(command.TenantId, command.Code, command.Name, command.Description),
            ct);

        return id;
    }
}
