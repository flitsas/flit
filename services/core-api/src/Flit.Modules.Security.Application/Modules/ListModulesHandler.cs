using Flit.Modules.Security.Domain.Modules;

namespace Flit.Modules.Security.Application.Modules;

public sealed class ListModulesHandler(ISecurityModuleRepository repository)
{
    public async Task<IReadOnlyList<SecurityModuleSummary>> HandleAsync(CancellationToken ct)
    {
        return await repository.ListAsync(ct);
    }
}
