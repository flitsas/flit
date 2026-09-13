using Flit.Admin.Application.Companies.UpdateCompany;

namespace Flit.Admin.Application.Companies.Children.UpdateChildCompany;

public sealed class UpdateChildCompanyCommand
{
    public required Guid HeadTenantId { get; init; }

    public required Guid ChildTenantId { get; init; }

    public required UpdateCompanyRequest Request { get; init; }

    public Guid? ChangedBy { get; init; }
}
