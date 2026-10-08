using Flit.Modules.Security.Application.Roles;
using Flit.Modules.Security.Domain.Roles;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Roles;

/// <summary>HU #13441 — CRUD de roles propios del tenant con tope de privilegios (AC1 a AC6).</summary>
public sealed class TenantRoleHandlersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();

    private static readonly PermissionInfo PermUsers = new(Guid.NewGuid(), "security.users.read", "Ver usuarios", "usuarios", "tramites");
    private static readonly PermissionInfo PermTramites = new(Guid.NewGuid(), "tramites.read", "Ver trámites", "tramites", "tramites");
    private static readonly PermissionInfo PermBanners = new(Guid.NewGuid(), "banners.manage", "Gestionar banners", "banners", "plataforma");
    private static readonly PermissionInfo PermComparendos = new(Guid.NewGuid(), "comparendos.read", "Ver comparendos", "comparendos", "comparendos");

    // Producto plataforma con un slug que NO está en el denylist (banners.*/superadmin.*): decisión del usuario,
    // todo el producto plataforma es no delegable.
    private static readonly PermissionInfo PermHub = new(Guid.NewGuid(), "usuarios.invite", "Invitar usuarios", "usuarios", "plataforma");

    private static readonly string[] Held = ["security.users.read", "tramites.read", "banners.manage", "comparendos.read", "usuarios.invite"];

    private readonly IRoleRepository _repo = Substitute.For<IRoleRepository>();

    public TenantRoleHandlersTests()
    {
        _repo.GetEnabledProductCodesAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string> { "plataforma", "tramites" });
        _repo.GetPermissionInfosAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var ids = ci.Arg<IReadOnlyList<Guid>>();
                return (IReadOnlyList<PermissionInfo>)new[] { PermUsers, PermTramites, PermBanners, PermComparendos, PermHub }
                    .Where(p => ids.Contains(p.Id)).ToList();
            });
        _repo.CreateAsync(Arg.Any<CreateRoleData>(), Arg.Any<CancellationToken>()).Returns(RoleId);
    }

    private static CreateTenantRoleCommand Create(
        string target = "COMPANY",
        string code = "contador",
        string product = "tramites",
        IReadOnlyList<Guid>? perms = null,
        string[]? caller = null) =>
        new(TenantId, target, code, "Contador", null, product, perms ?? [PermTramites.Id], caller ?? Held);

    private static RoleDetail Role(Guid? tenant, string product = "tramites") =>
        new(RoleId, "COMPANY", "contador", "Contador", null, false, true, [], product, tenant);

    // AC1
    [Fact]
    public async Task Crear_ConPermisosPermitidos_GuardaConTenantYPermisos()
    {
        var id = await new CreateTenantRoleHandler(_repo).HandleAsync(Create(), CancellationToken.None);

        id.Should().Be(RoleId);
        await _repo.Received(1).CreateAsync(
            Arg.Is<CreateRoleData>(d => d.TenantId == TenantId && d.TargetEntityType == "COMPANY" && d.Code == "contador"),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SetPermissionsAsync(
            RoleId, Arg.Is<IReadOnlyList<Guid>>(l => l.Single() == PermTramites.Id), Arg.Any<CancellationToken>());
    }

    // AC3 — permiso de plataforma, aunque el caller lo "tenga" en el token
    [Fact]
    public async Task Crear_ConPermisoDePlataforma_Responde_PLATFORM_ONLY_YNoGuarda()
    {
        var act = () => new CreateTenantRoleHandler(_repo).HandleAsync(Create(perms: [PermBanners.Id]), CancellationToken.None);

        (await act.Should().ThrowAsync<PrivilegeCeilingException>()).Which.Code.Should().Be(PrivilegeCeilingCodes.PlatformOnly);
        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(default!, Arg.Any<CancellationToken>());
    }

    // Decisión del usuario: TODO el producto plataforma es no delegable, aunque el slug no sea banners.*
    [Fact]
    public async Task Crear_ConCualquierPermisoDelProductoPlataforma_Responde_PLATFORM_ONLY()
    {
        var act = () => new CreateTenantRoleHandler(_repo).HandleAsync(Create(perms: [PermHub.Id]), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<PrivilegeCeilingException>()).Which;
        ex.Code.Should().Be(PrivilegeCeilingCodes.PlatformOnly);
        ex.Slugs.Should().Equal("usuarios.invite");
        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Crear_ConProductCodePlataforma_Responde_PLATFORM_ONLY()
    {
        var act = () => new CreateTenantRoleHandler(_repo).HandleAsync(Create(product: "plataforma", perms: []), CancellationToken.None);

        (await act.Should().ThrowAsync<PrivilegeCeilingException>()).Which.Code.Should().Be(PrivilegeCeilingCodes.PlatformOnly);
        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CambiarPermisos_ConPermisoDelProductoPlataforma_NoGuarda()
    {
        _repo.GetVisibleToTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(Role(TenantId));

        var act = () => new SetTenantRolePermissionsHandler(_repo).HandleAsync(
            new SetTenantRolePermissionsCommand(TenantId, RoleId, [PermHub.Id], Held), CancellationToken.None);

        (await act.Should().ThrowAsync<PrivilegeCeilingException>()).Which.Code.Should().Be(PrivilegeCeilingCodes.PlatformOnly);
        await _repo.DidNotReceiveWithAnyArgs().SetPermissionsAsync(default, default!, Arg.Any<CancellationToken>());
    }

    // HU #13440 (c): admin_<producto> está reservado aunque no haya fila global, sin importar mayúsculas
    [Theory]
    [InlineData("admin_tramites")]
    [InlineData("Admin_Comparendos")]
    [InlineData("ADMIN_DIAGNOSTICO")]
    public async Task Crear_ConCodeReservadoDeAdminDeProducto_LanzaRoleCodeDuplicate(string code)
    {
        var act = () => new CreateTenantRoleHandler(_repo).HandleAsync(Create(code: code), CancellationToken.None);

        await act.Should().ThrowAsync<RoleCodeDuplicateException>();
        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(default!, Arg.Any<CancellationToken>());
    }

    // AC3 — permiso que no posee
    [Fact]
    public async Task Crear_ConPermisoQueNoPosee_Responde_NOT_HELD()
    {
        var act = () => new CreateTenantRoleHandler(_repo)
            .HandleAsync(Create(caller: ["tramites.read"], perms: [PermTramites.Id, PermUsers.Id]), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<PrivilegeCeilingException>()).Which;
        ex.Code.Should().Be(PrivilegeCeilingCodes.NotHeld);
        ex.Slugs.Should().Equal("security.users.read");
        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(default!, Arg.Any<CancellationToken>());
    }

    // AC3 — módulo de un producto no habilitado para el tenant
    [Fact]
    public async Task Crear_ConPermisoDeProductoNoHabilitado_Responde_MODULE_NOT_ENABLED()
    {
        var act = () => new CreateTenantRoleHandler(_repo)
            .HandleAsync(Create(product: "comparendos", perms: [PermComparendos.Id]), CancellationToken.None);

        (await act.Should().ThrowAsync<PrivilegeCeilingException>()).Which.Code.Should().Be(PrivilegeCeilingCodes.ModuleNotEnabled);
    }

    [Fact]
    public async Task Crear_ConPermisoInexistente_Responde_NOT_FOUND()
    {
        var act = () => new CreateTenantRoleHandler(_repo).HandleAsync(Create(perms: [Guid.NewGuid()]), CancellationToken.None);

        (await act.Should().ThrowAsync<PrivilegeCeilingException>()).Which.Code.Should().Be(PrivilegeCeilingCodes.Unknown);
    }

    // AC4
    [Theory]
    [InlineData("TRANSIT_OFFICE")]
    [InlineData("OTRO")]
    public async Task Crear_ConTipoDistintoDeCompany_LanzaInvalidTargetEntityType(string target)
    {
        var act = () => new CreateTenantRoleHandler(_repo).HandleAsync(Create(target: target), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidTargetEntityTypeException>();
        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(default!, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("con espacio")]
    public async Task Crear_ConCodigoInvalido_LanzaInvalidRoleInput(string code)
    {
        var act = () => new CreateTenantRoleHandler(_repo).HandleAsync(Create(code: code), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRoleInputException>();
    }

    // HU #13440 AC2 — code repetido en el tenant o contra un rol global
    [Fact]
    public async Task Crear_ConCodeTomado_LanzaRoleCodeDuplicate()
    {
        _repo.CodeTakenForTenantAsync(TenantId, "contador", Arg.Any<CancellationToken>()).Returns(true);

        var act = () => new CreateTenantRoleHandler(_repo).HandleAsync(Create(), CancellationToken.None);

        await act.Should().ThrowAsync<RoleCodeDuplicateException>();
    }

    // AC2 — edición
    [Fact]
    public async Task CambiarPermisos_DeRolPropio_Aplica()
    {
        _repo.GetVisibleToTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(Role(TenantId));
        _repo.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(Role(TenantId));

        await new SetTenantRolePermissionsHandler(_repo).HandleAsync(
            new SetTenantRolePermissionsCommand(TenantId, RoleId, [PermTramites.Id], Held), CancellationToken.None);

        await _repo.Received(1).SetPermissionsAsync(RoleId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CambiarPermisos_ConPermisoDePlataforma_NoGuarda()
    {
        _repo.GetVisibleToTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(Role(TenantId));

        var act = () => new SetTenantRolePermissionsHandler(_repo).HandleAsync(
            new SetTenantRolePermissionsCommand(TenantId, RoleId, [PermBanners.Id], Held), CancellationToken.None);

        await act.Should().ThrowAsync<PrivilegeCeilingException>();
        await _repo.DidNotReceiveWithAnyArgs().SetPermissionsAsync(default, default!, Arg.Any<CancellationToken>());
    }

    // AC5 — rol global: visible pero de solo lectura
    [Fact]
    public async Task Editar_RolGlobal_LanzaRoleNotOwned()
    {
        _repo.GetVisibleToTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(Role(null));

        var set = () => new SetTenantRolePermissionsHandler(_repo).HandleAsync(
            new SetTenantRolePermissionsCommand(TenantId, RoleId, [], Held), CancellationToken.None);
        var upd = () => new UpdateTenantRoleHandler(_repo).HandleAsync(
            new UpdateTenantRoleCommand(TenantId, RoleId, "x", null), CancellationToken.None);
        var del = () => new DeleteTenantRoleHandler(_repo).HandleAsync(TenantId, RoleId, CancellationToken.None);

        await set.Should().ThrowAsync<RoleNotOwnedException>();
        await upd.Should().ThrowAsync<RoleNotOwnedException>();
        await del.Should().ThrowAsync<RoleNotOwnedException>();
        await _repo.DidNotReceiveWithAnyArgs().SoftDeleteAsync(default, Arg.Any<CancellationToken>());
    }

    // AC5 — rol de otro tenant: el repositorio no lo muestra, se reporta como inexistente
    [Fact]
    public async Task Editar_RolDeOtroTenant_LanzaRoleNotFound()
    {
        _repo.GetVisibleToTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>()).Returns((RoleDetail?)null);

        var del = () => new DeleteTenantRoleHandler(_repo).HandleAsync(TenantId, RoleId, CancellationToken.None);

        await del.Should().ThrowAsync<RoleNotFoundException>();
        await _repo.DidNotReceiveWithAnyArgs().SoftDeleteAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Editar_RolConTenantDistinto_LanzaRoleNotOwned()
    {
        _repo.GetVisibleToTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(Role(OtherTenantId));

        var del = () => new DeleteTenantRoleHandler(_repo).HandleAsync(TenantId, RoleId, CancellationToken.None);

        await del.Should().ThrowAsync<RoleNotOwnedException>();
    }

    // AC2 — eliminar
    [Fact]
    public async Task Eliminar_RolPropioSinUsuarios_HaceSoftDelete()
    {
        _repo.GetVisibleToTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(Role(TenantId));
        _repo.HasActiveUsersAsync(RoleId, Arg.Any<CancellationToken>()).Returns(false);

        await new DeleteTenantRoleHandler(_repo).HandleAsync(TenantId, RoleId, CancellationToken.None);

        await _repo.Received(1).SoftDeleteAsync(RoleId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Eliminar_RolAsignado_LanzaHasActiveUsers_Conflicto409()
    {
        _repo.GetVisibleToTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(Role(TenantId));
        _repo.HasActiveUsersAsync(RoleId, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => new DeleteTenantRoleHandler(_repo).HandleAsync(TenantId, RoleId, CancellationToken.None);

        await act.Should().ThrowAsync<RoleHasActiveUsersException>();
        await _repo.DidNotReceiveWithAnyArgs().SoftDeleteAsync(default, Arg.Any<CancellationToken>());
    }

    // Selector de permisos (HU #13443 AC2): solo lo que puede otorgar
    [Fact]
    public async Task PermisosOtorgables_ExcluyenPlataformaYLosQueNoPosee()
    {
        _repo.ListGrantablePermissionsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { PermUsers, PermTramites, PermBanners, PermHub });

        var result = await new ListGrantablePermissionsHandler(_repo)
            .HandleAsync(TenantId, ["tramites.read", "banners.manage", "usuarios.invite"], CancellationToken.None);

        result.Select(p => p.Slug).Should().Equal("tramites.read");
    }

    // AC5 — el listado lo resuelve el repositorio por tenant
    [Fact]
    public async Task Listar_DelegaEnElAlcanceDelTenant()
    {
        await new ListTenantRolesHandler(_repo).HandleAsync(TenantId, "COMPANY", CancellationToken.None);

        await _repo.Received(1).ListVisibleToTenantAsync(TenantId, "COMPANY", Arg.Any<CancellationToken>());
    }

    // El techo no depende del orden: plataforma gana sobre no poseído
    [Fact]
    public void PrivilegeCeiling_Plataforma_TieneProridadSobreNoPoseido()
    {
        var act = () => PrivilegeCeiling.Enforce(
            [PermBanners.Id], [PermBanners], [], new HashSet<string> { "plataforma" });

        act.Should().Throw<PrivilegeCeilingException>().Which.Code.Should().Be(PrivilegeCeilingCodes.PlatformOnly);
    }
}
