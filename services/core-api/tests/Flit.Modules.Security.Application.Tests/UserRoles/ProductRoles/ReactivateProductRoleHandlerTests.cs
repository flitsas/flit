using Flit.Modules.Security.Application.UserRoles.ProductRoles;
using Flit.Modules.Security.Domain.UserRoles.ProductRoles;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.UserRoles.ProductRoles;

/// <summary>
/// ADR-0089 / SP-12=a — reactivar en Diagnóstico recrea la asignación con el rol guardado y borra la marca; si el hub ya le dio
/// otro rol del producto, borra la marca y responde ROL_YA_VIGENTE.
/// </summary>
/// <remarks>Uso de ejemplo: <c>await new ReactivateProductRoleHandler(repo).HandleAsync(new ProductUserCommand(empresa, actor, "diagnostico", id), ct)</c>.</remarks>
public sealed class ReactivateProductRoleHandlerTests
{
    private const string P = "diagnostico";
    private static readonly Guid Empresa = Guid.Parse("00000000-0000-4000-8000-0000000000e1");
    private static readonly Guid Yo = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly ProductActor Admin = new(Yo, false);
    private static readonly ProductActor Super = new(Yo, true);

    private readonly FakeProductRoleRepository _repo = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<ProductRoleOutcome> Reactivar(ProductActor actor, Guid usuario) =>
        new ReactivateProductRoleHandler(_repo).HandleAsync(new ProductUserCommand(Empresa, actor, P, usuario), Ct);

    [Fact]
    public async Task Reactiva_ConElRolQueTeniaAlSuspenderse()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, rolDiagnostico: null, rolTramites: "user", suspendidoCon: "usuario_diagnostico");

        var outcome = await Reactivar(Admin, id);

        outcome.Changed.Should().BeTrue();
        outcome.RoleAfter.Should().Be("usuario_diagnostico");
        outcome.Row.State.Should().Be(ProductUserState.Active);
        var u = _repo.Get(id);
        u.Roles[P].Should().Be(FakeProductRoleRepository.UsuarioRol);
        u.Suspendido.Should().BeNull();
        u.Roles["tramites"].Should().Be(FakeProductRoleRepository.TramitesRol);
    }

    [Fact]
    public async Task YaActivo_EsIdempotente()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, "usuario_diagnostico");

        var outcome = await Reactivar(Admin, id);

        outcome.Changed.Should().BeFalse();
        _repo.Escrituras.Should().BeEmpty();
    }

    [Fact]
    public async Task ElHubLeDioOtroRol_BorraLaMarca_YRespondeRolYaVigente()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, "usuario_diagnostico", suspendidoCon: "usuario_diagnostico");

        var act = () => Reactivar(Super, id);

        (await act.Should().ThrowAsync<ProductRoleRuleException>()).Which.Should()
            .Match<ProductRoleRuleException>(e => e.Code == "ROL_YA_VIGENTE" && e.Kind == ProductRoleErrorKind.FailedPrecondition);
        _repo.Get(id).Suspendido.Should().BeNull();
        _repo.Escrituras.Should().Equal($"clear:{id}");
    }

    [Fact]
    public async Task AdminDelProducto_NoReactivaAUnAdminSuspendido()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, rolDiagnostico: null, suspendidoCon: "admin_diagnostico");

        var act = () => Reactivar(Admin, id);

        (await act.Should().ThrowAsync<ProductRoleRuleException>()).Which.Code.Should().Be("ROL_NO_ASIGNABLE");
        _repo.Escrituras.Should().BeEmpty();
    }

    [Fact]
    public async Task CuentaInactivaEnLaPlataforma_SeReactivaEnElProducto_ConMotivoCuentaInactiva()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, rolDiagnostico: null, suspendidoCon: "usuario_diagnostico").CuentaActiva = false;

        var outcome = await Reactivar(Super, id);

        outcome.Row.State.Should().Be(ProductUserState.Active);
        outcome.Row.Reason.Should().Be(NoActionsReason.AccountInactive);
        outcome.Row.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task SobreSiMismo_SeRechaza()
    {
        _repo.Miembro(Empresa, Yo, rolDiagnostico: null, suspendidoCon: "usuario_diagnostico");

        var act = () => Reactivar(Super, Yo);

        (await act.Should().ThrowAsync<ProductRoleRuleException>()).Which.Code.Should().Be("ACCION_SOBRE_SI_MISMO");
    }
}
