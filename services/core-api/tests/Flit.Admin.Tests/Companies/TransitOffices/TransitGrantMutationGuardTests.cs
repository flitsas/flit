using Flit.Admin.Application.Companies.TransitOffices;
using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;
using Flit.Admin.Domain.Companies.TransitOffices;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.TransitOffices;

/// <summary>HU #12346 — reglas de mutabilidad de habilitaciones OT (AC2, AC3, AC7).</summary>
public sealed class TransitGrantMutationGuardTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid HeadId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    [Fact]
    public async Task AC5_SuperAdmin_puede_agregar_sin_restriccion()
    {
        var hierarchy = Substitute.For<ICompanyHierarchyRepository>();
        var result = await TransitGrantMutationGuard.ValidateAddAsync(
            TenantId, isSuperAdmin: true, hierarchy, TestContext.Current.CancellationToken);

        result.IsAllowed.Should().BeTrue();
        await hierarchy.DidNotReceiveWithAnyArgs().GetHierarchyInfoAsync(default);
    }

    [Fact]
    public async Task AC3_Hijo_de_Concesion_no_puede_agregar_habilitacion_propia()
    {
        var hierarchy = Substitute.For<ICompanyHierarchyRepository>();
        hierarchy.GetHierarchyInfoAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(TenantId, CompanyTenantTypes.Concesionario, false, HeadId));
        hierarchy.GetHierarchyInfoAsync(HeadId, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(HeadId, CompanyTenantTypes.Concesion, true, null));

        var result = await TransitGrantMutationGuard.ValidateAddAsync(
            TenantId, isSuperAdmin: false, hierarchy, TestContext.Current.CancellationToken);

        result.IsAllowed.Should().BeFalse();
        result.Message.Should().Be(TransitGrantMutationGuard.HijoNoPuedeAgregarMessage);
    }

    [Fact]
    public async Task AC7_Cabeza_Concesion_no_puede_editar_su_lista()
    {
        var hierarchy = Substitute.For<ICompanyHierarchyRepository>();
        hierarchy.GetHierarchyInfoAsync(HeadId, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(HeadId, CompanyTenantTypes.Concesion, true, null));

        var add = await TransitGrantMutationGuard.ValidateAddAsync(
            HeadId, isSuperAdmin: false, hierarchy, TestContext.Current.CancellationToken);
        add.IsAllowed.Should().BeFalse();
        add.Message.Should().Be(TransitGrantMutationGuard.CabezaAsignadaPorSuperAdminMessage);

        var remove = await TransitGrantMutationGuard.ValidateRemoveAsync(
            HeadId, isSuperAdmin: false, hierarchy, TransitGrantSources.Client, TestContext.Current.CancellationToken);
        remove.IsAllowed.Should().BeFalse();
        remove.Message.Should().Be(TransitGrantMutationGuard.CabezaAsignadaPorSuperAdminMessage);
    }

    [Fact]
    public async Task AC2_Hijo_no_puede_eliminar_grant_SYSTEM()
    {
        var hierarchy = Substitute.For<ICompanyHierarchyRepository>();
        hierarchy.GetHierarchyInfoAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(TenantId, CompanyTenantTypes.Concesionario, false, HeadId));

        var result = await TransitGrantMutationGuard.ValidateRemoveAsync(
            TenantId,
            isSuperAdmin: false,
            hierarchy,
            TransitGrantSources.System,
            TestContext.Current.CancellationToken);

        result.IsAllowed.Should().BeFalse();
        result.Message.Should().Be(TransitGrantMutationGuard.GrantInmutableMessage);
    }

    [Fact]
    public async Task AC4_Cliente_sin_jerarquia_puede_agregar_y_quitar_grants_propios()
    {
        var hierarchy = Substitute.For<ICompanyHierarchyRepository>();
        hierarchy.GetHierarchyInfoAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(TenantId, CompanyTenantTypes.Renting, false, null));

        var add = await TransitGrantMutationGuard.ValidateAddAsync(
            TenantId, isSuperAdmin: false, hierarchy, TestContext.Current.CancellationToken);
        add.IsAllowed.Should().BeTrue();

        var remove = await TransitGrantMutationGuard.ValidateRemoveAsync(
            TenantId, isSuperAdmin: false, hierarchy, TransitGrantSources.Client, TestContext.Current.CancellationToken);
        remove.IsAllowed.Should().BeTrue();
    }
}
