using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.TransitBlocks.RemoveTransitBlock;

/// <summary>Baja de bloqueo de OT (HU #12407 AC2).</summary>
public sealed class RemoveTransitBlockHandler
{
    private readonly ICompanyHierarchyRepository _hierarchy;
    private readonly ITenantTransitOfficeBlockRepository _repository;

    public RemoveTransitBlockHandler(
        ICompanyHierarchyRepository hierarchy,
        ITenantTransitOfficeBlockRepository repository)
    {
        _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<(bool Removed, string? ErrorCode)> HandleAsync(
        RemoveTransitBlockCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var head = await _hierarchy
            .GetHierarchyInfoAsync(command.HeadTenantId, cancellationToken)
            .ConfigureAwait(false);

        if (head is null)
        {
            return (false, "not_found");
        }

        if (!string.Equals(head.TenantType, CompanyTenantTypes.MarcaBlanca, StringComparison.Ordinal)
            || head.ParentTenantId is not null)
        {
            return (false, "business_rule");
        }

        var removed = await _repository
            .RemoveBlockAsync(
                command.HeadTenantId,
                command.TransitOfficeId,
                command.ChangedBy,
                command.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);

        return removed ? (true, null) : (false, "not_found");
    }
}
