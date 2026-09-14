using Flit.Admin.Application.Companies.Hierarchy;
using Flit.Admin.Domain.Companies;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Hierarchy;

/// <summary>HU #12345 AC2/AC3 — parentesco explícito en handler.</summary>
public sealed class GroupHeadParenthoodGuardTests
{
    private static readonly Guid Head = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Child = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Foreign = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public async Task VerifyHeadCallerAsync_CoincideYEsCabeza_Permite()
    {
        var repo = Substitute.For<ICompanyHierarchyRepository>();
        repo.GetHierarchyInfoAsync(Head, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(Head, "CONCESION", true, null));

        var result = await GroupHeadParenthoodGuard.VerifyHeadCallerAsync(Head, Head, repo);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyHeadCallerAsync_HeadDistinto_Forbidden()
    {
        var repo = Substitute.For<ICompanyHierarchyRepository>();

        var result = await GroupHeadParenthoodGuard.VerifyHeadCallerAsync(Head, Foreign, repo);

        result.IsAllowed.Should().BeFalse();
        await repo.DidNotReceiveWithAnyArgs().GetHierarchyInfoAsync(default, default);
    }

    [Fact]
    public async Task VerifyChildOfHeadAsync_HijoPropio_Permite()
    {
        var repo = Substitute.For<ICompanyHierarchyRepository>();
        repo.GetHierarchyInfoAsync(Child, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(Child, "CONCESIONARIO", false, Head));

        var result = await GroupHeadParenthoodGuard.VerifyChildOfHeadAsync(Head, Child, repo);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyChildOfHeadAsync_ClienteAjeno_Forbidden()
    {
        var repo = Substitute.For<ICompanyHierarchyRepository>();
        repo.GetHierarchyInfoAsync(Foreign, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(Foreign, "CONCESIONARIO", false, null));

        var result = await GroupHeadParenthoodGuard.VerifyChildOfHeadAsync(Head, Foreign, repo);

        result.IsAllowed.Should().BeFalse();
    }
}
