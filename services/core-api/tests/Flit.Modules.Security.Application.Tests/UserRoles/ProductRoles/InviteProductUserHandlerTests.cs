using Flit.Modules.Security.Application.UserRoles.ProductRoles;
using Flit.Modules.Security.Domain.UserRoles.ProductRoles;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.UserRoles.ProductRoles;

/// <summary>
/// ADR-0089 regla 7 / SP-15 — invitar desde Diagnóstico: correo nuevo crea invitación con el rol resuelto por código; miembro
/// sin rol del producto recibe el rol (conserva el de Trámites); ya con rol, invitación pendiente o correo de otra empresa no
/// hacen nada y jamás mueven a nadie de empresa. Errores solo de forma o de autorización, nunca de existencia.
/// </summary>
/// <remarks>Uso de ejemplo: <c>await new InviteProductUserHandler(repo, sender).HandleAsync(new InviteProductUserCommand(...), ct)</c>.</remarks>
public sealed class InviteProductUserHandlerTests
{
    private const string P = "diagnostico";
    private static readonly Guid Empresa = Guid.Parse("00000000-0000-4000-8000-0000000000e1");
    private static readonly Guid OtraEmpresa = Guid.Parse("00000000-0000-4000-8000-0000000000e2");
    private static readonly Guid Yo = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly ProductActor Admin = new(Yo, false);
    private static readonly ProductActor Super = new(Yo, true);

    private readonly FakeProductRoleRepository _repo = new();
    private readonly SenderFalso _sender = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<InviteProductUserResult> Invitar(ProductActor actor, string email, string nombre = "Nueva Persona", string rol = "usuario_diagnostico") =>
        new InviteProductUserHandler(_repo, _sender).HandleAsync(new InviteProductUserCommand(Empresa, actor, P, email, nombre, rol), Ct);

    [Fact]
    public async Task CorreoNuevo_CreaInvitacionConElRolResueltoPorCodigo()
    {
        var result = await Invitar(Admin, "  Nueva@Ejemplo.Test ");

        result.Outcome.Should().Be(InviteOutcome.InvitationCreated);
        _sender.Enviados.Should().ContainSingle().Which.Should().Be((Empresa, "nueva@ejemplo.test", "Nueva Persona", FakeProductRoleRepository.UsuarioRol.Id, Yo));
    }

    [Fact]
    public async Task MiembroSinRolDelProducto_RecibeElRol_YConservaElDeTramites()
    {
        var id = Guid.NewGuid();
        _repo.Miembro(Empresa, id, rolDiagnostico: null, rolTramites: "user", email: "miembro@ejemplo.test");

        var result = await Invitar(Admin, "miembro@ejemplo.test");

        result.Outcome.Should().Be(InviteOutcome.RoleAssigned);
        _repo.Get(id).Roles[P].Should().Be(FakeProductRoleRepository.UsuarioRol);
        _repo.Get(id).Roles["tramites"].Should().Be(FakeProductRoleRepository.TramitesRol);
        _sender.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task YaConRol_Pendiente_YOtraEmpresa_NoHacenNada()
    {
        _repo.Miembro(Empresa, Guid.NewGuid(), "usuario_diagnostico", email: "ya@ejemplo.test");
        _repo.CorreosPendientes.Add("pendiente@ejemplo.test");
        _repo.Miembro(OtraEmpresa, Guid.NewGuid(), rolDiagnostico: null, rolTramites: "user", email: "ajeno@ejemplo.test");

        (await Invitar(Admin, "ya@ejemplo.test")).Outcome.Should().Be(InviteOutcome.AlreadyWithRole);
        (await Invitar(Admin, "pendiente@ejemplo.test")).Outcome.Should().Be(InviteOutcome.PendingInvitation);
        (await Invitar(Admin, "ajeno@ejemplo.test")).Outcome.Should().Be(InviteOutcome.OtherTenant);
        _repo.Escrituras.Should().BeEmpty("jamás se mueve a un usuario de empresa ni se toca a nadie");
        _sender.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task SiOtraInvitacionSeAdelanta_SeTrataComoPendiente()
    {
        _sender.Carrera = true;

        (await Invitar(Admin, "carrera@ejemplo.test")).Outcome.Should().Be(InviteOutcome.PendingInvitation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sin-arroba")]
    [InlineData("a@b")]
    [InlineData("con espacio@ejemplo.test")]
    public async Task CorreoInvalido_RecibeCorreoInvalido(string email)
    {
        var act = () => Invitar(Admin, email);

        (await act.Should().ThrowAsync<ProductRoleRuleException>()).Which.Should()
            .Match<ProductRoleRuleException>(e => e.Code == "CORREO_INVALIDO" && e.Kind == ProductRoleErrorKind.InvalidArgument);
    }

    [Fact]
    public async Task NombreVacioOLargo_RecibeNombreRequerido()
    {
        (await FluentActions.Awaiting(() => Invitar(Admin, "x@ejemplo.test", "   ")).Should().ThrowAsync<ProductRoleRuleException>()).Which.Code.Should().Be("NOMBRE_REQUERIDO");
        (await FluentActions.Awaiting(() => Invitar(Admin, "x@ejemplo.test", new string('n', 151))).Should().ThrowAsync<ProductRoleRuleException>()).Which.Code.Should().Be("NOMBRE_REQUERIDO");
    }

    [Fact]
    public async Task AdminDelProducto_InvitandoAdmin_RecibeRolNoAsignable_SuperAdminSiPuede()
    {
        (await FluentActions.Awaiting(() => Invitar(Admin, "x@ejemplo.test", rol: "admin_diagnostico")).Should().ThrowAsync<ProductRoleRuleException>())
            .Which.Code.Should().Be("ROL_NO_ASIGNABLE");
        _sender.Enviados.Should().BeEmpty();

        (await Invitar(Super, "x@ejemplo.test", rol: "admin_diagnostico")).Outcome.Should().Be(InviteOutcome.InvitationCreated);
        _sender.Enviados.Single().RoleId.Should().Be(FakeProductRoleRepository.AdminRol.Id);
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("SuperAdmin")]
    [InlineData("admin_tramites")]
    public async Task RolFueraDeLaListaBlanca_RecibeRolNoPermitido(string rol)
    {
        var act = () => Invitar(Super, "x@ejemplo.test", rol: rol);

        (await act.Should().ThrowAsync<ProductRoleRuleException>()).Which.Code.Should().Be("ROL_NO_PERMITIDO");
    }

    private sealed class SenderFalso : IProductInvitationSender
    {
        public List<(Guid Tenant, string Email, string Name, Guid RoleId, Guid InvitedBy)> Enviados { get; } = [];

        public bool Carrera { get; set; }

        public Task<ProductInvitationSent?> SendAsync(Guid tenantId, string email, string fullName, Guid roleId, Guid invitedBy, CancellationToken ct)
        {
            if (Carrera)
                return Task.FromResult<ProductInvitationSent?>(null);
            Enviados.Add((tenantId, email, fullName, roleId, invitedBy));
            return Task.FromResult<ProductInvitationSent?>(new ProductInvitationSent(Guid.NewGuid(), true));
        }
    }
}
