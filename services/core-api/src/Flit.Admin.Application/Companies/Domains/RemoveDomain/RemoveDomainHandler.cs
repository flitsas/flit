using Flit.Admin.Domain.Companies.Domains;

namespace Flit.Admin.Application.Companies.Domains.RemoveDomain;

/// <summary>Retira el dominio vigente de una red (HU #12416 AC5): la fila se conserva, deja de resolverse (AC4). Exclusivo SuperAdmin.</summary>
public sealed class RemoveDomainHandler(ITenantDomainRepository repository)
{
    private readonly ITenantDomainRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async Task<RemoveDomainResult> HandleAsync(RemoveDomainCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var retired = await _repository.RetireAsync(command.TenantId, command.ChangedBy, cancellationToken).ConfigureAwait(false);
        return retired is null ? RemoveDomainResult.NotFound() : RemoveDomainResult.Success(retired);
    }
}
