using Flit.Modules.Security.Application.UserRoles.ProductRoles;
using Flit.Modules.Security.Domain.UserRoles.ProductRoles;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.UserRoles.ProductRoles;

/// <summary>
/// ADR-0089 / SP-12=a — suspender en Diagnóstico cierra solo la asignación del producto y deja la marca; la cuenta y el rol de
/// Trámites siguen igual. Guarda propia del último admin, auto-acción, objetivo protegido y SP-11=a.
/// </summary>
/// <remarks>Uso de ejemplo: <c>await new SuspendProductRoleHandler(repo).HandleAsync(new ProductUserCommand(empresa, actor, "diagnostico", id), ct)</c>.</remarks>
public sealed class SuspendProductRoleHandlerTests
{
    private const string P = "diagnostico";
    private static readonly Guid Empresa = Guid.Parse("00000000-0000-4000-8000-0000000000e1");
    private static readonly Guid OtraEmpresa = Guid.Parse("00000000-0000-4000-8000-0000000000e2");
    private static readonly Guid Yo = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly ProductActor Admin = new(Yo, false);
    private static readonly ProductActor Super = new(Yo, true);

    private readonly FakeProductRoleRepository _repo = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<ProductRoleOutcome> Suspender(ProductActor actor, Guid usuario) =>
        new SuspendProductRoleHandler(_repo).HandleAsync(new ProductUserCommand(Empresa, actor, P, usuario), Ct);

    private async Task Rechaza(Func<Task> act, string code, ProductRoleErrorKind kind)
    {
        (await act.Should().ThrowAsync<ProductRoleRuleException>()).Which.Should().Match<ProductRoleRuleException>(e => e.Code == code && e.Kind == kind);
        _repo.Escrituras.Should().BeEmpty();
    }

    [Fact]
    public async Task Suspende_SoloEnElProducto_CuentaYRolDeTramitesIntactos()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, "usuario_diagnostico", rolTramites: "user");

        var outcome = await Suspender(Admin, id);

        outcome.Changed.Should().BeTrue();
        outcome.Row.State.Should().Be(ProductUserState.Suspended);
        outcome.Row.Actions.Should().Equal(ProductUserAction.Reactivate);
        outcome.RoleBefore.Should().Be("usuario_diagnostico");
        var u = _repo.Get(id);
        u.Suspendido.Should().Be(FakeProductRoleRepository.UsuarioRol, "la marca guarda el rol para reactivar");
        u.Roles.Should().NotContainKey(P);
        u.Roles["tramites"].Should().Be(FakeProductRoleRepository.TramitesRol, "sigue entrando a Trámites");
        u.CuentaActiva.Should().BeTrue("SP-12=a: no se suspende la cuenta");
    }

    [Fact]
    public async Task YaSuspendido_EsIdempotente()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, rolDiagnostico: null, suspendidoCon: "usuario_diagnostico");

        var outcome = await Suspender(Admin, id);

        outcome.Changed.Should().BeFalse();
        outcome.Row.State.Should().Be(ProductUserState.Suspended);
        _repo.Escrituras.Should().BeEmpty();
    }

    [Fact]
    public async Task ElUnicoAdmin_RecibeUltimoAdministrador()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, "admin_diagnostico");

        await Rechaza(() => Suspender(Super, id), "ULTIMO_ADMINISTRADOR", ProductRoleErrorKind.FailedPrecondition);
    }

    [Fact]
    public async Task AdminConOtroAdmin_LoSuspendeElSuperAdmin()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, "admin_diagnostico");
        _repo.Miembro(Empresa, Guid.NewGuid(), "admin_diagnostico");

        (await Suspender(Super, id)).Row.State.Should().Be(ProductUserState.Suspended);
    }

    [Fact]
    public async Task AdminDelProducto_SobreOtroAdmin_RecibeRolNoAsignable()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, "admin_diagnostico");
        _repo.Miembro(Empresa, Guid.NewGuid(), "admin_diagnostico");

        await Rechaza(() => Suspender(Admin, id), "ROL_NO_ASIGNABLE", ProductRoleErrorKind.PermissionDenied);
    }

    [Fact]
    public async Task SobreSiMismo_AdminCompany_YOtraEmpresa_SeRechazan()
    {
        _repo.Miembro(Empresa, Yo, "admin_diagnostico");
        var adminCompany = Guid.NewGuid();
        _repo.Miembro(Empresa, adminCompany, "admin_diagnostico", adminCompany: true);
        var ajeno = Guid.NewGuid();
        _repo.Miembro(OtraEmpresa, ajeno, "usuario_diagnostico");

        await Rechaza(() => Suspender(Admin, Yo), "ACCION_SOBRE_SI_MISMO", ProductRoleErrorKind.InvalidArgument);
        await Rechaza(() => Suspender(Super, adminCompany), "OBJETIVO_PROTEGIDO", ProductRoleErrorKind.PermissionDenied);
        await Rechaza(() => Suspender(Super, ajeno), "USUARIO_NO_ENCONTRADO", ProductRoleErrorKind.NotFound);
    }

    [Fact]
    public async Task InvitacionPendiente_RecibeInvitacionPendiente()
    {
        var invitacion = Guid.NewGuid();
        _repo.Invitaciones.Add((invitacion, Empresa, "Beto", "beto@ejemplo.test", "usuario_diagnostico"));

        await Rechaza(() => Suspender(Admin, invitacion), "INVITACION_PENDIENTE", ProductRoleErrorKind.FailedPrecondition);
    }

    [Fact]
    public async Task SuspenderUnAdmin_PideLaGuardaAtomica_UnUsuarioNo()
    {
        var admin = Guid.NewGuid();
        _repo.Miembro(Empresa, admin, "admin_diagnostico");
        _repo.Miembro(Empresa, Guid.NewGuid(), "admin_diagnostico");
        var usuario = Guid.NewGuid();
        _repo.Miembro(Empresa, usuario, "usuario_diagnostico");

        await Suspender(Super, admin);
        await Suspender(Super, usuario);

        _repo.Guardas.Should().Equal("admin_diagnostico", null);
    }

    [Fact]
    public async Task UsuarioConRolPropio_NoSeSuspende()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, "rol_propio_diagnostico");

        await Rechaza(() => Suspender(Super, id), "USUARIO_NO_ENCONTRADO", ProductRoleErrorKind.NotFound);
    }
}
