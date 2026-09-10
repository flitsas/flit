using Flit.Admin.Domain.Companies;

namespace Flit.Admin.Application.Companies.Hierarchy.UnlinkTenantParent;

/// <summary>HU #12355 AC3 — SuperAdmin desvincula un hijo sin pérdida de datos.</summary>
public sealed class UnlinkTenantParentHandler
{
    private readonly ICompanyHierarchyRepository _hierarchy;

    public UnlinkTenantParentHandler(ICompanyHierarchyRepository hierarchy)
    {
        _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
    }

    public async Task<UnlinkTenantParentResult> HandleAsync(
        UnlinkTenantParentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var updated = await _hierarchy
                .SetParentAsync(command.ChildTenantId, parentTenantId: null, command.ChangedBy, cancellationToken)
                .ConfigureAwait(false);

            return updated is null
                ? UnlinkTenantParentResult.NotFound()
                : UnlinkTenantParentResult.Unlinked(updated);
        }
        catch (CompanyLinkRejectedException ex)
        {
            return UnlinkTenantParentResult.Rejected(ex.Message);
        }
    }
}

public enum UnlinkTenantParentOutcome
{
    Unlinked,
    NotFound,
    Rejected,
}

public sealed class UnlinkTenantParentResult
{
    private UnlinkTenantParentResult(UnlinkTenantParentOutcome outcome, CompanyListItem? company, string message)
    {
        Outcome = outcome;
        Company = company;
        Message = message;
    }

    public UnlinkTenantParentOutcome Outcome { get; }

    public CompanyListItem? Company { get; }

    public string Message { get; }

    public static UnlinkTenantParentResult Unlinked(CompanyListItem company) =>
        new(UnlinkTenantParentOutcome.Unlinked, company, string.Empty);

    public static UnlinkTenantParentResult NotFound() =>
        new(UnlinkTenantParentOutcome.NotFound, null, string.Empty);

    public static UnlinkTenantParentResult Rejected(string message) =>
        new(UnlinkTenantParentOutcome.Rejected, null, message);
}
