using Flit.Admin.Application.Companies.CreateCompany;

namespace Flit.Admin.Application.Companies.Children.CreateChildCompany;

public sealed class CreateChildCompanyCommand
{
    public required Guid HeadTenantId { get; init; }

    public required CreateCompanyRequest Request { get; init; }

    public Guid? CreatedBy { get; init; }

    /// <summary>Tipo de la cabeza (CONCESION / MARCA_BLANCA) para resolver default del hijo.</summary>
    public required string HeadTenantType { get; init; }
}
