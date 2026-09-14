using Flit.Admin.Application.Companies.Hierarchy.LinkTenantParent;
using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Hierarchy;

/// <summary>HU #12355 AC2/AC4 — vínculo SuperAdmin.</summary>
public sealed class LinkTenantParentHandlerTests
{
    private static readonly Guid Child = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Parent = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public async Task AC4_HijoTipoCabeza_Rechazado422()
    {
        var repo = Substitute.For<ICompanyHierarchyRepository>();
        repo.GetHierarchyInfoAsync(Child, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(Child, HeadTenantTypes.Concesion, true, null));

        var handler = new LinkTenantParentHandler(repo);
        var result = await handler.HandleAsync(new LinkTenantParentCommand(Child, Parent, null));

        result.Outcome.Should().Be(LinkTenantParentOutcome.Invalid);
        result.Field.Should().Be("tenantType");
        await repo.DidNotReceiveWithAnyArgs().SetParentAsync(default, default, default, default);
    }

    [Fact]
    public async Task AC2_VinculoValido_Exitoso()
    {
        var repo = Substitute.For<ICompanyHierarchyRepository>();
        repo.GetHierarchyInfoAsync(Child, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(Child, ChildTenantTypes.Concesionario, false, null));
        repo.GetHierarchyInfoAsync(Parent, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(Parent, HeadTenantTypes.Concesion, true, null));
        repo.SetParentAsync(Child, Parent, null, Arg.Any<CancellationToken>())
            .Returns(new CompanyListItem { Id = Child, TenantType = ChildTenantTypes.Concesionario });

        var handler = new LinkTenantParentHandler(repo);
        var result = await handler.HandleAsync(new LinkTenantParentCommand(Child, Parent, null));

        result.Outcome.Should().Be(LinkTenantParentOutcome.Linked);
    }
}
