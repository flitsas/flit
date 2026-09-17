using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using Xunit;

namespace Flit.Analytics.Application.Tests.Network;

/// <summary>
/// HU #12652 (Feature #12257) — regla pura de rol de la red: <see cref="NetworkScopePolicy.ValidateRole"/>
/// devuelve <c>null</c> solo si alguno de los roles activos es AdminCompany; si no,
/// <see cref="NetworkScopePolicy.RoleRequired"/>. Es independiente del alcance (que se valida antes).
/// Uso de ejemplo: <c>NetworkScopePolicy.ValidateRole(["Radicador", "AdminCompany"])</c> ⇒ <c>null</c>.
/// </summary>
public sealed class NetworkScopePolicyRoleTests
{
    [Fact]
    public void Codigos_del_contrato_no_cambian()
    {
        NetworkScopePolicy.RoleRequired.Should().Be("network_role_required");
        NetworkScopePolicy.ScopeRequired.Should().Be("network_scope_required");
        NetworkScopePolicy.HeadAdminRole.Should().Be("AdminCompany");
        NetworkScopePolicy.RoleRequired.Should().NotBe(NetworkScopePolicy.ScopeRequired,
            "el frontend distingue «no tienes red» de «tienes red pero no eres su administrador»");
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("admincompany")]
    [InlineData("ADMINCOMPANY")]
    public void AdminCompany_pasa_sin_distinguir_mayusculas(string role)
    {
        NetworkScopePolicy.ValidateRole([role]).Should().BeNull();
    }

    [Fact]
    public void Multi_rol_con_AdminCompany_en_cualquier_posicion_pasa()
    {
        NetworkScopePolicy.ValidateRole(["Radicador", "AdminCompany"]).Should().BeNull();
        NetworkScopePolicy.ValidateRole(["AdminCompany", "Radicador", "Operador"]).Should().BeNull();
        NetworkScopePolicy.ValidateRole(["Radicador", "Radicador", "AdminCompany", "AdminCompany"]).Should().BeNull("claims role + role_code duplican cada rol");
    }

    [Theory]
    [InlineData("Radicador")]
    [InlineData("Operador")]
    [InlineData("Gestor")]
    [InlineData("ot_admin")]
    [InlineData("SuperAdmin")]
    [InlineData("AdminCompany ")]
    [InlineData("Admin")]
    public void Otro_rol_role_required(string role)
    {
        NetworkScopePolicy.ValidateRole([role]).Should().Be(NetworkScopePolicy.RoleRequired);
    }

    [Fact]
    public void Sin_roles_o_multi_rol_sin_AdminCompany_role_required()
    {
        NetworkScopePolicy.ValidateRole([]).Should().Be(NetworkScopePolicy.RoleRequired);
        NetworkScopePolicy.ValidateRole(["Radicador", "Operador", "Gestor"]).Should().Be(NetworkScopePolicy.RoleRequired);
    }

    [Fact]
    public void Roles_nulos_lanzan()
    {
        var act = () => NetworkScopePolicy.ValidateRole(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void El_alcance_se_valida_antes_que_el_rol_hija_y_SuperAdmin_siguen_en_scope_required()
    {
        // Orden del GroupHeadReadFilter: primero Validate(scope), después ValidateRole. Una hija (Single)
        // o un SuperAdmin (All) nunca llegan a la puerta de rol aunque su rol sea AdminCompany.
        var hija = TenantScope.Single(Guid.NewGuid());
        NetworkScopePolicy.Validate(hija).Should().Be(NetworkScopePolicy.ScopeRequired);
        NetworkScopePolicy.ValidateRole(["AdminCompany"]).Should().BeNull("el rol por sí solo no abre la red");

        var cabeza = TenantScope.Group(Guid.NewGuid(), [Guid.NewGuid()], GroupKind.MarcaBlanca);
        NetworkScopePolicy.Validate(cabeza).Should().BeNull();
        NetworkScopePolicy.ValidateRole(["Radicador"]).Should().Be(NetworkScopePolicy.RoleRequired, "cabeza MARCA_BLANCA con rol no-admin");
    }
}
