using Flit.Modules.Security.Application.UserRoles.ProductRoles;
using Flit.Modules.Security.Domain.UserRoles.ProductRoles;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.UserRoles.ProductRoles;

/// <summary>
/// ADR-0089 / SP-9=a — cambiar el rol de Diagnóstico solo toca la asignación del producto (el rol de Trámites se conserva),
/// con lista blanca, SP-11=a, objetivo protegido, auto-acción, IDOR → NOT_FOUND y último admin.
/// </summary>
/// <remarks>Uso de ejemplo: <c>await new AssignProductRoleHandler(repo).HandleAsync(new ChangeProductRoleCommand(...), ct)</c>.</remarks>
public sealed class AssignProductRoleHandlerTests
{
    private const string P = "diagnostico";
    private static readonly Guid Empresa = Guid.Parse("00000000-0000-4000-8000-0000000000e1");
    private static readonly Guid OtraEmpresa = Guid.Parse("00000000-0000-4000-8000-0000000000e2");
    private static readonly Guid Yo = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly ProductActor Admin = new(Yo, false);
    private static readonly ProductActor Super = new(Yo, true);

    private readonly FakeProductRoleRepository _repo = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<ProductRoleOutcome> Cambiar(ProductActor actor, Guid usuario, string rol, Guid? empresa = null) =>
        new AssignProductRoleHandler(_repo).HandleAsync(new ChangeProductRoleCommand(empresa ?? Empresa, actor, P, usuario, rol), Ct);

    private async Task Rechaza(Func<Task> act, string code, ProductRoleErrorKind kind)
    {
        (await act.Should().ThrowAsync<ProductRoleRuleException>()).Which.Should().Match<ProductRoleRuleException>(e => e.Code == code && e.Kind == kind);
        _repo.Escrituras.Should().BeEmpty();
    }

    [Fact]
    public async Task SuperAdmin_DaAdminDiagnostico_ConservaElRolDeTramites()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, "usuario_diagnostico", rolTramites: "user");
        _repo.Miembro(Empresa, Guid.NewGuid(), "admin_diagnostico");

        var outcome = await Cambiar(Super, id, "admin_diagnostico");

        outcome.Changed.Should().BeTrue();
        outcome.RoleBefore.Should().Be("usuario_diagnostico");
        outcome.RoleAfter.Should().Be("admin_diagnostico");
        outcome.Row.RoleCode.Should().Be("admin_diagnostico");
        _repo.Get(id).Roles["tramites"].Should().Be(FakeProductRoleRepository.TramitesRol, "SP-9=a: el rol de otro producto no se toca");
        _repo.Escrituras.Should().Equal($"replace:{id}:diagnostico");
    }

    [Fact]
    public async Task SuperAdmin_QuitaAdmin_HabiendoOtroAdmin()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, "admin_diagnostico");
        _repo.Miembro(Empresa, Guid.NewGuid(), "admin_diagnostico");

        var outcome = await Cambiar(Super, id, "usuario_diagnostico");

        outcome.RoleAfter.Should().Be("usuario_diagnostico");
    }

    [Fact]
    public async Task MismoRol_EsIdempotente_SinEscribir()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, "usuario_diagnostico");

        var outcome = await Cambiar(Admin, id, "usuario_diagnostico");

        outcome.Changed.Should().BeFalse();
        _repo.Escrituras.Should().BeEmpty();
    }

    [Fact]
    public async Task AdminDelProducto_QueIntentaDarAdmin_RecibeRolNoAsignable()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, "usuario_diagnostico");

        await Rechaza(() => Cambiar(Admin, id, "admin_diagnostico"), "ROL_NO_ASIGNABLE", ProductRoleErrorKind.PermissionDenied);
    }

    [Fact]
    public async Task AdminDelProducto_SobreOtroAdmin_RecibeRolNoAsignable()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, "admin_diagnostico");
        _repo.Miembro(Empresa, Guid.NewGuid(), "admin_diagnostico");

        await Rechaza(() => Cambiar(Admin, id, "usuario_diagnostico"), "ROL_NO_ASIGNABLE", ProductRoleErrorKind.PermissionDenied);
    }

    [Fact]
    public async Task ObjetivoAdminCompany_RecibeObjetivoProtegido()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, "admin_diagnostico", adminCompany: true);

        await Rechaza(() => Cambiar(Super, id, "usuario_diagnostico"), "OBJETIVO_PROTEGIDO", ProductRoleErrorKind.PermissionDenied);
    }

    [Fact]
    public async Task SobreSiMismo_RecibeAccionSobreSiMismo()
    {
        _repo.Miembro(Empresa, Yo, "admin_diagnostico");

        await Rechaza(() => Cambiar(Super, Yo, "usuario_diagnostico"), "ACCION_SOBRE_SI_MISMO", ProductRoleErrorKind.InvalidArgument);
    }

    [Fact]
    public async Task UsuarioDeOtraEmpresa_YUsuarioSinRolDelProducto_RecibenElMismoNotFound()
    {
        var ajeno = Guid.NewGuid();
        _repo.Miembro(OtraEmpresa, ajeno, "usuario_diagnostico");
        var soloTramites = Guid.NewGuid();
        _repo.Miembro(Empresa, soloTramites, rolDiagnostico: null, rolTramites: "user");

        await Rechaza(() => Cambiar(Super, ajeno, "usuario_diagnostico"), "USUARIO_NO_ENCONTRADO", ProductRoleErrorKind.NotFound);
        await Rechaza(() => Cambiar(Super, soloTramites, "usuario_diagnostico"), "USUARIO_NO_ENCONTRADO", ProductRoleErrorKind.NotFound);
        await Rechaza(() => Cambiar(Super, Guid.NewGuid(), "usuario_diagnostico"), "USUARIO_NO_ENCONTRADO", ProductRoleErrorKind.NotFound);
    }

    [Fact]
    public async Task IdDeInvitacionPendiente_RecibeInvitacionPendiente()
    {
        var invitacion = Guid.NewGuid();
        _repo.Invitaciones.Add((invitacion, Empresa, "Beto", "beto@ejemplo.test", "usuario_diagnostico"));

        await Rechaza(() => Cambiar(Super, invitacion, "admin_diagnostico"), "INVITACION_PENDIENTE", ProductRoleErrorKind.FailedPrecondition);
    }

    [Fact]
    public async Task UsuarioSuspendido_RecibeUsuarioSuspendido()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, rolDiagnostico: null, suspendidoCon: "usuario_diagnostico");

        await Rechaza(() => Cambiar(Super, id, "admin_diagnostico"), "USUARIO_SUSPENDIDO", ProductRoleErrorKind.FailedPrecondition);
    }

    [Fact]
    public async Task QuitarElUnicoAdmin_RecibeUltimoAdministrador()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, "admin_diagnostico");

        await Rechaza(() => Cambiar(Super, id, "usuario_diagnostico"), "ULTIMO_ADMINISTRADOR", ProductRoleErrorKind.FailedPrecondition);
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("SuperAdmin")]
    [InlineData("admin_tramites")]
    [InlineData("rol_propio_de_otra_compania")]
    public async Task RolFueraDeLaListaBlanca_RecibeRolNoPermitido(string rol)
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, "usuario_diagnostico");

        await Rechaza(() => Cambiar(Super, id, rol), "ROL_NO_PERMITIDO", ProductRoleErrorKind.InvalidArgument);
    }
}
