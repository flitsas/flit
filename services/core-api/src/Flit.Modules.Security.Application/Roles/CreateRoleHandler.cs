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

        // HU #13653: sin code, el servidor lo genera a partir del nombre (identificador interno, no se ve en pantalla).
        if (string.IsNullOrWhiteSpace(command.Code))
        {
            return await RoleCodeGenerator.CreateWithGeneratedCodeAsync(
                repository,
                tenantId: null,
                command.TargetEntityType,
                command.Name,
                reserved: [],
                code => new CreateRoleData(command.TargetEntityType, code, command.Name, command.Description, command.ProductCode),
                ct);
        }

        // Compatibilidad: un code explícito (clientes o scripts existentes) se respeta como antes.
        var codeExists = await repository.CodeExistsAsync(command.TargetEntityType, command.Code, ct);
        if (codeExists)
            throw new RoleCodeDuplicateException();

        var id = await repository.CreateAsync(
            new CreateRoleData(command.TargetEntityType, command.Code, command.Name, command.Description, command.ProductCode),
            ct);

        return id;
    }
}
