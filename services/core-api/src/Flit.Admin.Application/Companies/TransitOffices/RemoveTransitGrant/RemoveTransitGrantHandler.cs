using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.RemoveTransitGrant;

/// <summary>
/// Caso de uso de la baja de un grant de organismo de tránsito (HU #10192, AC3; HU #12346).
/// </summary>
public sealed class RemoveTransitGrantHandler
{
    private readonly ICompanyHierarchyRepository _hierarchy;
    private readonly ITransitGrantRepository _repository;

    public RemoveTransitGrantHandler(
        ICompanyHierarchyRepository hierarchy,
        ITransitGrantRepository repository)
    {
        _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<RemoveTransitGrantResult> HandleAsync(
        RemoveTransitGrantCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var source = await _repository
            .GetGrantSourceAsync(command.TenantId, command.TransitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        if (source is null)
        {
            return RemoveTransitGrantResult.NotFound();
        }

        var guard = await TransitGrantMutationGuard
            .ValidateRemoveAsync(
                command.TenantId,
                command.IsSuperAdmin,
                _hierarchy,
                source,
                cancellationToken)
            .ConfigureAwait(false);

        if (!guard.IsAllowed)
        {
            return RemoveTransitGrantResult.BusinessDenied(guard.Message!);
        }

        var removed = await _repository
            .RemoveGrantAsync(
                command.TenantId, command.TransitOfficeId, command.ChangedBy, command.CorrelationId, cancellationToken)
            .ConfigureAwait(false);

        return removed ? RemoveTransitGrantResult.Success() : RemoveTransitGrantResult.NotFound();
    }
}
