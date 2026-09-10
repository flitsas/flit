namespace Flit.Admin.Application.Companies.Hierarchy.LinkTenantParent;

public sealed record LinkTenantParentCommand(
    Guid ChildTenantId,
    Guid ParentTenantId,
    Guid? ChangedBy);
