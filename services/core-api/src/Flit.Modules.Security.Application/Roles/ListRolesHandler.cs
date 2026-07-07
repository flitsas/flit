using Flit.Modules.Security.Domain.Roles;

namespace Flit.Modules.Security.Application.Roles;

public sealed class ListRolesHandler(IRoleRepository repository)
{
    public async Task<IReadOnlyList<RoleSummary>> HandleAsync(string targetEntityType, CancellationToken ct)
    {
        return await repository.ListByTargetEntityTypeAsync(targetEntityType, ct);
    }
}
