using Flit.Admin.Application.Companies.SetCompanyStatus;
using Flit.Admin.Domain.Companies;

namespace Flit.Admin.Application.Companies.Children.SetChildCompanyStatus;

/// <summary>HU #12345 AC2 — activar/desactivar hijo propio.</summary>
public sealed class SetChildCompanyStatusHandler
{
    private readonly ICompanyWriteRepository _repository;

    public SetChildCompanyStatusHandler(ICompanyWriteRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<SetCompanyStatusResult> HandleAsync(
        SetChildCompanyStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var company = await _repository
            .SetActiveAsync(command.ChildTenantId, command.EstadoActivo, command.ChangedBy, cancellationToken)
            .ConfigureAwait(false);

        return company is null
            ? SetCompanyStatusResult.NotFound()
            : SetCompanyStatusResult.Updated(company);
    }
}
