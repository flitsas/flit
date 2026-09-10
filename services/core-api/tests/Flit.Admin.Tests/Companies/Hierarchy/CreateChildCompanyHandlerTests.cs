using Flit.Admin.Application.Companies.Children.CreateChildCompany;
using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Hierarchy;

/// <summary>HU #12345 AC1 — alta de hijo con tipos permitidos.</summary>
public sealed class CreateChildCompanyHandlerTests
{
    private static readonly Guid Head = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public async Task AC1_TipoCabeza_Rechazado422()
    {
        var hierarchy = Substitute.For<ICompanyHierarchyRepository>();
        var companies = Substitute.For<ICompanyWriteRepository>();
        companies.CodeExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        companies.TaxIdExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var handler = new CreateChildCompanyHandler(hierarchy, companies);
        var result = await handler.HandleAsync(new CreateChildCompanyCommand
        {
            HeadTenantId = Head,
            HeadTenantType = HeadTenantTypes.Concesion,
            Request = new CreateCompanyRequest("Hijo", "900100200-1", "HIJO-1", "CONCESION", true),
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "tenantType");
        await hierarchy.DidNotReceiveWithAnyArgs().CreateChildAsync(default!, default);
    }

    [Fact]
    public async Task AC1_DefaultConcesion_EsConcesionario()
    {
        var hierarchy = Substitute.For<ICompanyHierarchyRepository>();
        var companies = Substitute.For<ICompanyWriteRepository>();
        companies.CodeExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        companies.TaxIdExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var expected = new CompanyListItem { Id = Guid.NewGuid(), Code = "HIJO-2", TenantType = ChildTenantTypes.Concesionario };
        hierarchy.CreateChildAsync(Arg.Any<NewChildCompany>(), Arg.Any<CancellationToken>()).Returns(expected);

        var handler = new CreateChildCompanyHandler(hierarchy, companies);
        var result = await handler.HandleAsync(new CreateChildCompanyCommand
        {
            HeadTenantId = Head,
            HeadTenantType = HeadTenantTypes.Concesion,
            Request = new CreateCompanyRequest("Hijo", "900100200-2", "HIJO-2", null, true),
        });

        result.IsValid.Should().BeTrue();
        await hierarchy.Received(1).CreateChildAsync(
            Arg.Is<NewChildCompany>(c => c.TenantType == ChildTenantTypes.Concesionario && c.ParentTenantId == Head),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC9_DefaultMarcaBlanca_EsRenting()
    {
        var hierarchy = Substitute.For<ICompanyHierarchyRepository>();
        var companies = Substitute.For<ICompanyWriteRepository>();
        companies.CodeExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        companies.TaxIdExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        hierarchy.CreateChildAsync(Arg.Any<NewChildCompany>(), Arg.Any<CancellationToken>())
            .Returns(new CompanyListItem { Id = Guid.NewGuid(), TenantType = ChildTenantTypes.Renting });

        var handler = new CreateChildCompanyHandler(hierarchy, companies);
        await handler.HandleAsync(new CreateChildCompanyCommand
        {
            HeadTenantId = Head,
            HeadTenantType = HeadTenantTypes.MarcaBlanca,
            Request = new CreateCompanyRequest("Hijo", "900100200-3", "HIJO-3", null, true),
        });

        await hierarchy.Received(1).CreateChildAsync(
            Arg.Is<NewChildCompany>(c => c.TenantType == ChildTenantTypes.Renting),
            Arg.Any<CancellationToken>());
    }
}
