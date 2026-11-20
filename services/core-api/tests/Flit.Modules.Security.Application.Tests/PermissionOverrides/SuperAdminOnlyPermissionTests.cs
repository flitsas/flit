using Flit.Modules.Security.Application.PermissionOverrides;
using Flit.Modules.Security.Application.Roles;
using Flit.Modules.Security.Domain.PermissionOverrides;
using Flit.Modules.Security.Domain.Roles;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.PermissionOverrides;

/// <summary>
/// Épica #14644 (HU #14648), hallazgo de seguridad: <c>runt_contingency.manage</c> afecta a TODAS las empresas, así que solo el rol
/// SuperAdmin puede tenerlo. Ni un rol de empresa, ni un usuario por permiso individual (ni siquiera concedido por el Super Admin),
/// ni el catálogo otorgable lo ofrecen.
/// </summary>
public sealed class SuperAdminOnlyPermissionTests
{
    private const string Slug = "runt_contingency.manage";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid PermissionId = Guid.NewGuid();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("GRANT", true, "SUPERADMIN_ONLY")]
    [InlineData("DENY", true, "SUPERADMIN_ONLY")]
    [InlineData("GRANT", false, "SUPERADMIN_ONLY")]
    [InlineData("REVOKE", true, null)]
    [InlineData("READ", true, null)]
    public async Task Regla_UsuarioNoRecibeElPermiso_NiDelSuperAdmin(string action, bool actorEsSuperAdmin, string? esperado)
    {
        var request = new PermissionOverrideRequest(Tenant, new OverrideActor(Guid.NewGuid(), actorEsSuperAdmin), Guid.NewGuid(), action, Slug, "excluded");

        (await new SuperAdminOnlyPermissionRule().CheckAsync(request, Ct)).Should().Be(esperado);
    }

    [Fact]
    public async Task Regla_OtrosPermisosNoSeAfectan()
    {
        var request = new PermissionOverrideRequest(Tenant, new OverrideActor(Guid.NewGuid(), true), Guid.NewGuid(), "GRANT", "logqx.read", "true");

        (await new SuperAdminOnlyPermissionRule().CheckAsync(request, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Catalogo_ElPermisoNoSeOfreceParaOtorgarAUnUsuario_AunSiElTopeEsIlimitado()
    {
        var repo = Substitute.For<IUserPermissionOverrideRepository>();
        repo.ListEnforcedPermissionsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new PermissionCatalogEntry(PermissionId, Slug, "Contingencia", "contingencia-runt", "plataforma", "true"),
            new PermissionCatalogEntry(Guid.NewGuid(), "logqx.read", "Log QX", "logqx", "plataforma", "true"),
        ]);
        var sut = new ManageUserPermissionOverridesHandler(repo, new PermissionCapCalculator(repo), new Application.PermissionOverrides.GrantModule.GrantModuleOverridesHandler(repo), []);

        var list = await sut.ListGrantableAsync(Tenant, new OverrideActor(Guid.NewGuid(), true), Ct);

        list.Select(p => p.Slug).Should().BeEquivalentTo(["logqx.read"]);
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("ot_admin")]
    [InlineData("Operador")]
    public async Task Rol_ConElPermisoQueNoEsSuperAdmin_SeRechaza_YNoSeGuarda(string codigoRol)
    {
        var repo = Substitute.For<IRoleRepository>();
        repo.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(new RoleDetail(RoleId, "COMPANY", codigoRol, codigoRol, null, false, true, [], "plataforma"));
        repo.GetPermissionInfosAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new PermissionInfo(PermissionId, Slug, "Contingencia", "contingencia-runt", "plataforma")]);

        var act = () => new SetRolePermissionsHandler(repo).HandleAsync(new SetRolePermissionsCommand(RoleId, [PermissionId]), Ct);

        (await act.Should().ThrowAsync<SuperAdminOnlyPermissionException>()).Which.Slugs.Should().Equal(Slug);
        await repo.DidNotReceiveWithAnyArgs().SetPermissionsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rol_SuperAdmin_SiPuedeLlevarElPermiso()
    {
        var repo = Substitute.For<IRoleRepository>();
        var superAdmin = new RoleDetail(RoleId, "COMPANY", "SuperAdmin", "SuperAdmin", null, true, true, [], "plataforma");
        repo.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(superAdmin, superAdmin);
        repo.GetPermissionProductCodesAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(["plataforma"]);

        await new SetRolePermissionsHandler(repo).HandleAsync(new SetRolePermissionsCommand(RoleId, [PermissionId]), Ct);

        await repo.Received(1).SetPermissionsAsync(RoleId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RolDeEmpresa_ElTechoDePrivilegiosTambienLoBloquea_AunSiElLlamadorLoPosee()
    {
        var info = new PermissionInfo(PermissionId, Slug, "Contingencia", "contingencia-runt", "tramites");

        var act = () => PrivilegeCeiling.Enforce([PermissionId], [info], [Slug], new HashSet<string> { "tramites" });

        act.Should().Throw<PrivilegeCeilingException>().Which.Code.Should().Be(PrivilegeCeilingCodes.PlatformOnly);
    }
}
