using Flit.Modules.Security.Application.Products;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Products;

/// <summary>
/// HU #12967 — el admin de cada producto es espejo de AdminCompany: las pantallas de usuarios y roles no lo ofrecen ni
/// lo muestran como rol principal, sea cual sea el producto.
/// </summary>
public sealed class ProductRoleCodesTests
{
    [Theory]
    [InlineData("admin_tramites")]
    [InlineData("admin_comparendos")]
    [InlineData("admin_diagnostico")]
    public void El_admin_de_cada_producto_es_espejo(string code) =>
        ProductRoleCodes.IsProductAdmin(code).Should().BeTrue();

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("SuperAdmin")]
    [InlineData("Radicador")]
    [InlineData("admin_plataforma")]
    [InlineData("admin_otro")]
    [InlineData("Admin_tramites")]
    [InlineData("")]
    [InlineData(null)]
    public void Los_demas_roles_no_son_espejo(string? code) =>
        ProductRoleCodes.IsProductAdmin(code).Should().BeFalse();
}
