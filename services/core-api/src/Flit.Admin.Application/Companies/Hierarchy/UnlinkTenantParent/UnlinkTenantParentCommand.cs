namespace Flit.Admin.Application.Companies.Hierarchy.UnlinkTenantParent;

public sealed record UnlinkTenantParentCommand(Guid ChildTenantId, Guid? ChangedBy);
