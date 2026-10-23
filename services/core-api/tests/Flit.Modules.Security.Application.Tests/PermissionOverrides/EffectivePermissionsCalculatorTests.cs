using Flit.Modules.Security.Domain.PermissionOverrides;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.PermissionOverrides;

/// <summary>HU #13643 AC1-AC3 — calculador único de permisos efectivos (rol + allow - deny, producto &gt; usuario &gt; rol).</summary>
public sealed class EffectivePermissionsCalculatorTests
{
    private static readonly string[] Role = ["tramites.read", "tramites.create"];

    private static EffectivePermissionOverride Allow(string slug, string product = "tramites") => new(slug, product, PermissionEffects.Allow);

    private static EffectivePermissionOverride Deny(string slug, string product = "tramites") => new(slug, product, PermissionEffects.Deny);

    [Fact]
    public void AC1_SinOverrides_DevuelveLosPermisosDelRolSinCambios()
    {
        var result = EffectivePermissionsCalculator.Compute(Role, [], _ => true);

        result.Should().Equal(Role);
    }

    [Fact]
    public void AC2_AllowSuma_DenyResta()
    {
        var result = EffectivePermissionsCalculator.Compute(Role, [Allow("reportes.read"), Deny("tramites.read")], _ => true);

        result.Should().Contain("reportes.read").And.NotContain("tramites.read").And.Contain("tramites.create");
    }

    [Fact]
    public void AC3_AllowDeProductoNoHabilitado_NoApareceYDenySiResta()
    {
        var result = EffectivePermissionsCalculator.Compute(
            Role,
            [Allow("comparendos.read", "comparendos"), Allow("reportes.read"), Deny("tramites.create")],
            p => p == "tramites");

        result.Should().NotContain("comparendos.read").And.Contain("reportes.read").And.NotContain("tramites.create");
    }

    [Fact]
    public void AllowQueYaTieneElRol_NoSeDuplica_YDenyGanaSiConvivenConAllow()
    {
        var result = EffectivePermissionsCalculator.Compute(Role, [Allow("tramites.read"), Allow("x.y"), Deny("x.y")], _ => true);

        result.Should().Equal("tramites.read", "tramites.create");
    }

    [Fact]
    public void DenyDeUnPermisoQueElRolNoTiene_NoHaceNada()
    {
        EffectivePermissionsCalculator.Compute(Role, [Deny("otro.slug")], _ => true).Should().Equal(Role);
    }
}
