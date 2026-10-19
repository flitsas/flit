using Flit.Api.Authorization;
using Flit.Modules.Security.Domain.PermissionOverrides;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// Hallazgo review Épica #12753: <see cref="IPermissionEnforcementState"/> descubre de los endpoints reales los slugs que solo se
/// exigen con <c>Security:ModulePermissionEnforcement:Enabled</c> (sin lista a mano) y los <c>ot.*</c> con el flag OT.
/// </summary>
public sealed class PermissionEnforcementStateTests
{
    private static IPermissionEnforcementState State(WebApplicationFactory<Program> f) =>
        f.Services.GetRequiredService<IPermissionEnforcementState>();

    [Theory]
    [InlineData("reportes.read")]
    [InlineData("reportes.uso.read")]
    [InlineData("reportes.programacion.manage")]
    public void Con_el_flag_de_modulos_apagado_los_slugs_de_modulo_no_se_exigen(string slug)
    {
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("Security:ModulePermissionEnforcement:Enabled", "false"));

        State(host).IsEffectivelyEnforced(slug).Should().BeFalse();
    }

    [Theory]
    [InlineData("reportes.read")]
    [InlineData("reportes.uso.read")]
    [InlineData("reportes.programacion.manage")]
    public void Con_el_flag_de_modulos_encendido_los_slugs_de_modulo_se_exigen(string slug)
    {
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("Security:ModulePermissionEnforcement:Enabled", "true"));

        State(host).IsEffectivelyEnforced(slug).Should().BeTrue();
    }

    [Fact]
    public void Los_slugs_ot_dependen_del_flag_OT_y_no_del_de_modulos()
    {
        using var apagado = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("Security:OtPermissionEnforcement:Enabled", "false").UseSetting("Security:ModulePermissionEnforcement:Enabled", "true"));
        using var encendido = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("Security:OtPermissionEnforcement:Enabled", "true").UseSetting("Security:ModulePermissionEnforcement:Enabled", "false"));

        State(apagado).IsEffectivelyEnforced(OtPermissionMap.Tramites).Should().BeFalse();
        State(encendido).IsEffectivelyEnforced(OtPermissionMap.Tramites).Should().BeTrue();
    }

    [Fact]
    public void Un_slug_sin_flag_siempre_se_exige()
    {
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("Security:ModulePermissionEnforcement:Enabled", "false").UseSetting("Security:OtPermissionEnforcement:Enabled", "false"));

        State(host).IsEffectivelyEnforced("ict.pii.reveal").Should().BeTrue();
    }
}
