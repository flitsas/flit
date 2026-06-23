using Flit.Modules.Security.Domain.Permissions;

namespace Flit.Modules.Security.Application.Permissions;

public sealed class CreatePermissionHandler(IPermissionRepository repository)
{
    public async Task<Guid> HandleAsync(CreatePermissionCommand command, CancellationToken ct)
    {
        var moduleIsActive = await repository.ModuleExistsAndIsActiveAsync(command.ModuleId, ct);
        if (!moduleIsActive)
            throw new ModuleInactiveException();

        var slugExists = await repository.SlugExistsAsync(command.Slug, ct);
        if (slugExists)
            throw new PermissionSlugDuplicateException();

        var id = await repository.CreateAsync(
            new CreatePermissionData(
                command.ModuleId,
                command.Slug,
                command.Name,
                command.Action,
                command.HttpMethod,
                command.RoutePattern,
                command.Description),
            ct);

        return id;
    }
}
