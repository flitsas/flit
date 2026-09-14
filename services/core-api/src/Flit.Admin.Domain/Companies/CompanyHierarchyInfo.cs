namespace Flit.Admin.Domain.Companies;

/// <summary>Proyección mínima de jerarquía sobre <c>identity.tenants</c> (HU #12345 / #12355).</summary>
public sealed record CompanyHierarchyInfo(
    Guid TenantId,
    string TenantType,
    bool IsGroupParent,
    Guid? ParentTenantId);
