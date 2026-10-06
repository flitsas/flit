using Flit.Modules.Security.Application.Products;
using Flit.Modules.Security.Domain.Roles;

namespace Flit.Modules.Security.Application.Roles;

public sealed class CreateRoleHandler(IRoleRepository repository)
{
    public async Task<Guid> HandleAsync(CreateRoleCommand command, CancellationToken ct)
    {
        if (command.TargetEntityType is not ("COMPANY" or "TRANSIT_OFFICE"))
            throw new InvalidTargetEntityTypeException();

        // HU #12964: el rol pertenece a un producto del contrato §1.
        if (!ProductCodes.All.Contains(command.ProductCode, StringComparer.Ordinal))
            throw new InvalidRoleProductException();

        var codeExists = await repository.CodeExistsAsync(command.TargetEntityType, command.Code, ct);
        if (codeExists)
            throw new RoleCodeDuplicateException();

        var id = await repository.CreateAsync(
            new CreateRoleData(command.TargetEntityType, command.Code, command.Name, command.Description, command.ProductCode),
            ct);

        return id;
    }
}
