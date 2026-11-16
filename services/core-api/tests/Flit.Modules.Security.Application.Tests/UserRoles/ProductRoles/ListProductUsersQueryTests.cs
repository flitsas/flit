using Flit.Modules.Security.Application.UserRoles.ProductRoles;
using Flit.Modules.Security.Domain.UserRoles.ProductRoles;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.UserRoles.ProductRoles;

/// <summary>
/// ADR-0089 / SP-13 / SP-18 — listado de usuarios de Diagnóstico de UNA empresa: activos, invitados y suspendidos del producto
/// (ni usuarios sin rol del producto ni los de otra empresa), orden, búsqueda sin tildes, filtros y paginación con tope.
/// </summary>
/// <remarks>Uso de ejemplo: <c>await new ListProductUsersHandler(repo).HandleAsync(new ListProductUsersQuery(...), ct)</c>.</remarks>
public sealed class ListProductUsersQueryTests
{
    private const string P = "diagnostico";
    private static readonly Guid Empresa = Guid.Parse("00000000-0000-4000-8000-0000000000e1");
    private static readonly Guid OtraEmpresa = Guid.Parse("00000000-0000-4000-8000-0000000000e2");
    private static readonly Guid Yo = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly ProductActor Admin = new(Yo, false);
    private static readonly ProductActor Super = new(Yo, true);

    private readonly FakeProductRoleRepository _repo = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<ProductUsersPage> Listar(ProductActor actor, string? busqueda = null, string[]? roles = null,
        ProductUserState[]? estados = null, int pagina = 0, int tamano = 0) =>
        new ListProductUsersHandler(_repo).HandleAsync(new ListProductUsersQuery(Empresa, actor, P, busqueda, roles ?? [], estados ?? [], pagina, tamano), Ct);

    private void Sembrar()
    {
        _repo.Miembro(Empresa, Yo, "admin_diagnostico", nombre: "Zoe Admin", email: "zoe@ejemplo.test");
        _repo.Miembro(Empresa, Guid.NewGuid(), "usuario_diagnostico", nombre: "Ángela Ruiz", email: "angela@ejemplo.test");
        _repo.Miembro(Empresa, Guid.NewGuid(), rolDiagnostico: null, suspendidoCon: "usuario_diagnostico", nombre: "Carlos Suspendido");
        _repo.Miembro(Empresa, Guid.NewGuid(), rolDiagnostico: null, rolTramites: "user", nombre: "Solo Tramites");
        _repo.Miembro(OtraEmpresa, Guid.NewGuid(), "usuario_diagnostico", nombre: "De Otra Empresa");
        _repo.Invitaciones.Add((Guid.NewGuid(), Empresa, "Beto Invitado", "beto@ejemplo.test", "usuario_diagnostico"));
        _repo.Invitaciones.Add((Guid.NewGuid(), OtraEmpresa, "Invitado Ajeno", "ajeno@ejemplo.test", "usuario_diagnostico"));
    }

    [Fact]
    public async Task ListaSoloLosDelProductoYLaEmpresa_OrdenEstadoYNombre()
    {
        Sembrar();

        var page = await Listar(Admin);

        page.Total.Should().Be(4);
        page.Users.Select(u => u.Name).Should().Equal("Ángela Ruiz", "Zoe Admin", "Beto Invitado", "Carlos Suspendido");
        page.Users.Select(u => u.State).Should().Equal(ProductUserState.Active, ProductUserState.Active, ProductUserState.Invited, ProductUserState.Suspended);
        page.AssignableRoles.Should().Equal("usuario_diagnostico");
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(25);
    }

    [Fact]
    public async Task AccionesYRolesAsignables_SeCalculanParaElActor()
    {
        Sembrar();

        var admin = await Listar(Admin);
        var super = await Listar(Super);

        admin.Users.Single(u => u.Name == "Zoe Admin").Reason.Should().Be(NoActionsReason.Self);
        admin.Users.Single(u => u.Name == "Ángela Ruiz").Actions.Should().Equal(ProductUserAction.Suspend);
        super.Users.Single(u => u.Name == "Ángela Ruiz").Actions.Should().Equal(ProductUserAction.ChangeRole, ProductUserAction.Suspend);
        super.AssignableRoles.Should().Equal("usuario_diagnostico", "admin_diagnostico");
    }

    [Fact]
    public async Task Busqueda_SinTildesNiMayusculas_SobreNombreOCorreo()
    {
        Sembrar();

        (await Listar(Admin, busqueda: "ANGELA R")).Users.Select(u => u.Name).Should().Equal(["Ángela Ruiz"], "solo el nombre lleva «ángela r»: sin tilde ni mayúsculas");
        (await Listar(Admin, busqueda: "beto@")).Users.Select(u => u.Name).Should().Equal("Beto Invitado");
    }

    [Fact]
    public async Task FiltrosDeRolYEstado()
    {
        Sembrar();

        (await Listar(Admin, roles: ["admin_diagnostico"])).Users.Select(u => u.Name).Should().Equal("Zoe Admin");
        (await Listar(Admin, estados: [ProductUserState.Suspended, ProductUserState.Invited])).Total.Should().Be(2);
    }

    [Fact]
    public async Task Paginacion_DevuelveElTramoYElTotal()
    {
        Sembrar();

        var page = await Listar(Admin, pagina: 2, tamano: 3);

        page.Total.Should().Be(4);
        page.Users.Select(u => u.Name).Should().Equal("Carlos Suspendido");
    }

    [Theory]
    [InlineData(-1, 25)]
    [InlineData(1, 101)]
    [InlineData(1, -5)]
    [InlineData(101, 100)]
    public async Task PaginacionFueraDeRango_RecibePaginacionInvalida(int pagina, int tamano)
    {
        var act = () => Listar(Admin, pagina: pagina, tamano: tamano);

        (await act.Should().ThrowAsync<ProductRoleRuleException>()).Which.Code.Should().Be("PAGINACION_INVALIDA");
    }

    [Fact]
    public async Task FiltroConRolAjeno_OBusquedaLarga_RecibeFiltroInvalido()
    {
        (await FluentActions.Awaiting(() => Listar(Admin, roles: ["AdminCompany"])).Should().ThrowAsync<ProductRoleRuleException>())
            .Which.Code.Should().Be("FILTRO_INVALIDO");
        (await FluentActions.Awaiting(() => Listar(Admin, busqueda: new string('a', 101))).Should().ThrowAsync<ProductRoleRuleException>())
            .Which.Code.Should().Be("FILTRO_INVALIDO");
    }

    [Fact]
    public async Task ConteoPorEmpresa_UsuariosYAdmins()
    {
        Sembrar();

        var conteos = await new ListProductUsersHandler(_repo).CountByTenantAsync(P, Ct);

        conteos.Single(c => c.TenantId == Empresa).Should().Be(new ProductTenantCount(Empresa, 3, 1));
    }
}
