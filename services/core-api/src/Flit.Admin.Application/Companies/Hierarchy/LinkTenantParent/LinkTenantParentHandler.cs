using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;

namespace Flit.Admin.Application.Companies.Hierarchy.LinkTenantParent;

/// <summary>HU #12355 AC2/AC4 — SuperAdmin vincula un cliente existente como hijo.</summary>
public sealed class LinkTenantParentHandler
{
    private readonly ICompanyHierarchyRepository _hierarchy;

    public LinkTenantParentHandler(ICompanyHierarchyRepository hierarchy)
    {
        _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
    }

    public async Task<LinkTenantParentResult> HandleAsync(
        LinkTenantParentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var child = await _hierarchy
            .GetHierarchyInfoAsync(command.ChildTenantId, cancellationToken)
            .ConfigureAwait(false);

        if (child is null)
        {
            return LinkTenantParentResult.NotFound();
        }

        if (HeadTenantTypes.IsHead(child.TenantType))
        {
            return LinkTenantParentResult.Invalid(
                "tenantType",
                "No se puede vincular como hijo a un cliente con tipo de cabeza de grupo.");
        }

        var parent = await _hierarchy
            .GetHierarchyInfoAsync(command.ParentTenantId, cancellationToken)
            .ConfigureAwait(false);

        if (parent is not { IsGroupParent: true, ParentTenantId: null })
        {
            return LinkTenantParentResult.Invalid(
                "parentTenantId",
                "El cliente padre debe ser una cabeza de grupo activa sin padre.");
        }

        try
        {
            var updated = await _hierarchy
                .SetParentAsync(command.ChildTenantId, command.ParentTenantId, command.ChangedBy, cancellationToken)
                .ConfigureAwait(false);

            return updated is null
                ? LinkTenantParentResult.NotFound()
                : LinkTenantParentResult.Linked(updated);
        }
        catch (CompanyLinkRejectedException ex)
        {
            return LinkTenantParentResult.Invalid("parentTenantId", ex.Message);
        }
    }
}

public enum LinkTenantParentOutcome
{
    Linked,
    NotFound,
    Invalid,
}

public sealed class LinkTenantParentResult
{
    private LinkTenantParentResult(LinkTenantParentOutcome outcome, CompanyListItem? company, string field, string message)
    {
        Outcome = outcome;
        Company = company;
        Field = field;
        Message = message;
    }

    public LinkTenantParentOutcome Outcome { get; }

    public CompanyListItem? Company { get; }

    public string Field { get; }

    public string Message { get; }

    public static LinkTenantParentResult Linked(CompanyListItem company) =>
        new(LinkTenantParentOutcome.Linked, company, string.Empty, string.Empty);

    public static LinkTenantParentResult NotFound() =>
        new(LinkTenantParentOutcome.NotFound, null, string.Empty, string.Empty);

    public static LinkTenantParentResult Invalid(string field, string message) =>
        new(LinkTenantParentOutcome.Invalid, null, field, message);
}
