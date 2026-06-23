using Flit.Modules.Security.Domain.Roles;

namespace Flit.Modules.Security.Application.Roles;

public sealed class ListRolesHandler(IRoleRepository repository)
{
    public async Task<IReadOnlyList<RoleSummary>> HandleAsync(Guid tenantId, CancellationToken ct)
    {
        return await repository.ListByTenantAsync(tenantId, ct);
    }
}
