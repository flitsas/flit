using Flit.Admin.Domain.Companies;

namespace Flit.Admin.Application.Companies.SetCompanyStatus;

/// <summary>
/// Caso de uso para activar/desactivar una compañía (#10118). Delega el cambio de
/// <c>identity.tenants.is_active</c> al repositorio (idempotente) y traduce la ausencia
/// del tenant a <see cref="SetCompanyStatusOutcome.NotFound"/>.
/// </summary>
public sealed class SetCompanyStatusHandler
{
    private readonly ICompanyWriteRepository _repository;

    public SetCompanyStatusHandler(ICompanyWriteRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<SetCompanyStatusResult> HandleAsync(
        SetCompanyStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var company = await _repository
            .SetActiveAsync(command.TenantId, command.EstadoActivo, command.ChangedBy, cancellationToken)
            .ConfigureAwait(false);

        return company is null
            ? SetCompanyStatusResult.NotFound()
            : SetCompanyStatusResult.Updated(company);
    }
}
