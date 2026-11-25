using Flit.Modules.Security.Application.UserRoles.ProductRoles;
using Flit.Modules.Security.Domain.UserRoles.ProductRoles;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.UserRoles.ProductRoles;

/// <summary>
/// ADR-0089 (HU #14587) — reglas puras de usuarios por producto: lista blanca por código (SP-10), solo el SuperAdmin da el
/// admin del producto (SP-11=a), objetivos protegidos y auto-acción (SP-12) y acciones calculadas por actor.
/// </summary>
/// <remarks>Uso de ejemplo: <c>ProductRoleRules.RowFor(actor, "diagnostico", miembro).Actions</c>.</remarks>
public sealed class ProductRoleRulesTests
{
    private const string P = "diagnostico";
    private static readonly Guid Yo = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly ProductActor Admin = new(Yo, IsSuperAdmin: false);
    private static readonly ProductActor Super = new(Yo, IsSuperAdmin: true);

    private static ProductMemberSnapshot Miembro(Guid? id = null, ProductRoleRef? rol = null, ProductRoleRef? suspendido = null,
        bool activa = true, bool adminCompany = false, bool superAdmin = false) =>
        new(id ?? Guid.NewGuid(), "Ana", "ana@ejemplo.test", activa, adminCompany, superAdmin, rol, suspendido);

    [Fact]
    public void RolesAsignables_SuperAdmin_LosDos_AdminDelProducto_SoloUsuario()
    {
        ProductRoleRules.AssignableRoles(Super, P).Should().Equal("usuario_diagnostico", "admin_diagnostico");
        ProductRoleRules.AssignableRoles(Admin, P).Should().Equal("usuario_diagnostico");
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("SuperAdmin")]
    [InlineData("admin_tramites")]
    [InlineData("user")]
    [InlineData("usuario_comparendos")]
    [InlineData("")]
    [InlineData(null)]
    public void ListaBlanca_RechazaCualquierRolQueNoSeaDelProducto(string? rol)
    {
        var act = () => ProductRoleRules.EnsureAllowedRole(P, rol);

        act.Should().Throw<ProductRoleRuleException>().Where(e => e.Code == "ROL_NO_PERMITIDO" && e.Kind == ProductRoleErrorKind.InvalidArgument);
    }

    [Fact]
    public void AdminDelProducto_NoPuedeDarElAdmin_SuperAdminSi()
    {
        var act = () => ProductRoleRules.EnsureCanGrant(Admin, P, "admin_diagnostico");

        act.Should().Throw<ProductRoleRuleException>().Where(e => e.Code == "ROL_NO_ASIGNABLE" && e.Kind == ProductRoleErrorKind.PermissionDenied);
        FluentActions.Invoking(() => ProductRoleRules.EnsureCanGrant(Super, P, "admin_diagnostico")).Should().NotThrow();
        FluentActions.Invoking(() => ProductRoleRules.EnsureCanGrant(Admin, P, "usuario_diagnostico")).Should().NotThrow();
    }

    [Fact]
    public void UltimoAdmin_SeProtege_SoloSiQuedariaSinOtro()
    {
        FluentActions.Invoking(() => ProductRoleRules.EnsureNotLastAdmin(P, "admin_diagnostico", 0))
            .Should().Throw<ProductRoleRuleException>().Where(e => e.Code == "ULTIMO_ADMINISTRADOR" && e.Kind == ProductRoleErrorKind.FailedPrecondition);
        FluentActions.Invoking(() => ProductRoleRules.EnsureNotLastAdmin(P, "admin_diagnostico", 1)).Should().NotThrow();
        FluentActions.Invoking(() => ProductRoleRules.EnsureNotLastAdmin(P, "usuario_diagnostico", 0)).Should().NotThrow();
    }

    [Fact]
    public void Fila_UsuarioActivo_ParaAdminDelProducto_SoloSuspender_ParaSuperAdmin_CambiarYSuspender()
    {
        var m = Miembro(rol: new ProductRoleRef(Guid.NewGuid(), "usuario_diagnostico"));

        ProductRoleRules.RowFor(Admin, P, m).Actions.Should().Equal(ProductUserAction.Suspend);
        var fila = ProductRoleRules.RowFor(Super, P, m);
        fila.Actions.Should().Equal(ProductUserAction.ChangeRole, ProductUserAction.Suspend);
        fila.State.Should().Be(ProductUserState.Active);
        fila.Reason.Should().Be(NoActionsReason.None);
    }

    [Fact]
    public void Fila_Suspendida_SoloReactivar_ConElRolGuardado()
    {
        var fila = ProductRoleRules.RowFor(Admin, P, Miembro(suspendido: new ProductRoleRef(Guid.NewGuid(), "usuario_diagnostico")));

        fila.State.Should().Be(ProductUserState.Suspended);
        fila.RoleCode.Should().Be("usuario_diagnostico");
        fila.Actions.Should().Equal(ProductUserAction.Reactivate);
    }

    [Fact]
    public void Fila_SinAcciones_ConSuMotivo()
    {
        var usuario = new ProductRoleRef(Guid.NewGuid(), "usuario_diagnostico");
        var admin = new ProductRoleRef(Guid.NewGuid(), "admin_diagnostico");

        ProductRoleRules.RowFor(Admin, P, Miembro(Yo, usuario)).Reason.Should().Be(NoActionsReason.Self);
        ProductRoleRules.RowFor(Super, P, Miembro(rol: admin, adminCompany: true)).Reason.Should().Be(NoActionsReason.AdminCompany);
        ProductRoleRules.RowFor(Super, P, Miembro(rol: admin, superAdmin: true)).Reason.Should().Be(NoActionsReason.AdminCompany);
        ProductRoleRules.RowFor(Super, P, Miembro(rol: usuario, activa: false)).Reason.Should().Be(NoActionsReason.AccountInactive);
        ProductRoleRules.RowFor(Admin, P, Miembro(rol: admin)).Reason.Should().Be(NoActionsReason.SuperAdminOnly);
        ProductRoleRules.RowFor(Admin, P, Miembro(Yo, usuario)).Actions.Should().BeEmpty();
    }

    [Fact]
    public void Fila_DeInvitacion_EsInvitado_SinAcciones()
    {
        var fila = ProductRoleRules.RowFor(new ProductInvitationSnapshot(Guid.NewGuid(), "Beto", "beto@ejemplo.test", "usuario_diagnostico"));

        fila.IsInvitation.Should().BeTrue();
        fila.State.Should().Be(ProductUserState.Invited);
        fila.Actions.Should().BeEmpty();
        fila.Reason.Should().Be(NoActionsReason.Invitation);
    }

    [Fact]
    public void ObjetivoProtegido_YAutoAccion_SeRechazan()
    {
        FluentActions.Invoking(() => ProductRoleRules.EnsureNotProtected(Miembro(adminCompany: true)))
            .Should().Throw<ProductRoleRuleException>().Where(e => e.Code == "OBJETIVO_PROTEGIDO" && e.Kind == ProductRoleErrorKind.PermissionDenied);
        FluentActions.Invoking(() => ProductRoleRules.EnsureNotSelf(Admin, Yo))
            .Should().Throw<ProductRoleRuleException>().Where(e => e.Code == "ACCION_SOBRE_SI_MISMO" && e.Kind == ProductRoleErrorKind.InvalidArgument);
    }

    [Fact]
    public void PermisoDeGestion_EsDelManifiestoDelProducto() =>
        ProductRoleRules.ManagePermission(P).Should().Be("diagnostico.usuarios.gestionar");

    [Fact]
    public void RolPropioDeLaCompania_NoEsVisible_NiLoTocaUnAdminDelProducto()
    {
        var propio = new ProductRoleRef(Guid.NewGuid(), "rol_propio_diagnostico");

        ProductRoleRules.IsVisible(P, Miembro(rol: propio)).Should().BeFalse("CR-1: solo roles de la lista blanca");
        ProductRoleRules.IsVisible(P, Miembro(suspendido: propio)).Should().BeFalse();
        ProductRoleRules.IsVisible(P, Miembro(rol: new ProductRoleRef(Guid.NewGuid(), "usuario_diagnostico"))).Should().BeTrue();
        FluentActions.Invoking(() => ProductRoleRules.EnsureCanTouch(Admin, P, Miembro(rol: propio)))
            .Should().Throw<ProductRoleRuleException>().Where(e => e.Code == "ROL_NO_ASIGNABLE");
        ProductRoleRules.RowFor(Admin, P, Miembro(rol: propio)).Reason.Should().Be(NoActionsReason.SuperAdminOnly);
    }
    // ── HU #14694 (M13) — los roles de cada producto salen de su manifiesto: Impuestos usa gestor_impuestos, no usuario_impuestos ──

    [Fact]
    public void Impuestos_SusRolesSonLosDelManifiesto_GestorYAdmin()
    {
        ProductRoleRules.UserRole("impuestos").Should().Be("gestor_impuestos");
        ProductRoleRules.AdminRole("impuestos").Should().Be("admin_impuestos");
        ProductRoleRules.AssignableRoles(Super, "impuestos").Should().Equal("gestor_impuestos", "admin_impuestos");
        ProductRoleRules.AssignableRoles(Admin, "impuestos").Should().Equal("gestor_impuestos");
        FluentActions.Invoking(() => ProductRoleRules.EnsureAllowedRole("impuestos", "gestor_impuestos")).Should().NotThrow();
        FluentActions.Invoking(() => ProductRoleRules.EnsureCanGrant(Admin, "impuestos", "admin_impuestos"))
            .Should().Throw<ProductRoleRuleException>().Where(e => e.Code == "ROL_NO_ASIGNABLE");
    }

    [Theory]
    [InlineData("usuario_impuestos")]
    [InlineData("usuario_diagnostico")]
    [InlineData("admin_diagnostico")]
    [InlineData("gestor_diagnostico")]
    public void Impuestos_RechazaElRolGenericoYLosDeOtroProducto(string rol) =>
        FluentActions.Invoking(() => ProductRoleRules.EnsureAllowedRole("impuestos", rol))
            .Should().Throw<ProductRoleRuleException>().Where(e => e.Code == "ROL_NO_PERMITIDO");

    [Fact]
    public void Impuestos_UnAdminDelProductoSoloTocaGestores_YDiagnosticoNoCambia()
    {
        var gestor = new ProductRoleRef(Guid.NewGuid(), "gestor_impuestos");
        ProductRoleRules.IsVisible("impuestos", Miembro(rol: gestor)).Should().BeTrue();
        ProductRoleRules.RowFor(Admin, "impuestos", Miembro(rol: gestor)).Actions.Should().Equal(ProductUserAction.Suspend);
        FluentActions.Invoking(() => ProductRoleRules.EnsureCanTouch(Admin, "impuestos", Miembro(rol: gestor))).Should().NotThrow();
        ProductRoleRules.IsVisible("impuestos", Miembro(rol: new ProductRoleRef(Guid.NewGuid(), "usuario_impuestos"))).Should().BeFalse();
        ProductRoleRules.UserRole(P).Should().Be("usuario_diagnostico", "Diagnóstico conserva su rol de usuario");
    }
}
