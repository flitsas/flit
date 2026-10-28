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
public sealed class PermissionEnforcementStateTests(PermissionEnforcementStateTests.Hosts hosts) : IClassFixture<PermissionEnforcementStateTests.Hosts>
{
    /// <summary>
    /// Las pruebas solo leen el estado (un singleton calculado al arrancar), así que los hosts se comparten por
    /// configuración: 3 en lugar de uno o dos por prueba (21). Sin la bandera, ambas quedan apagadas por TestEnvironment.
    /// </summary>
    public sealed class Hosts : IDisposable
    {
        private readonly Lazy<WebApplicationFactory<Program>> _modulos = Host(("Security:ModulePermissionEnforcement:Enabled", "true"), ("Security:OtPermissionEnforcement:Enabled", "false"));
        private readonly Lazy<WebApplicationFactory<Program>> _ot = Host(("Security:OtPermissionEnforcement:Enabled", "true"), ("Security:ModulePermissionEnforcement:Enabled", "false"));
        private readonly Lazy<WebApplicationFactory<Program>> _ninguno = Host(("Security:ModulePermissionEnforcement:Enabled", "false"), ("Security:OtPermissionEnforcement:Enabled", "false"));

        private static Lazy<WebApplicationFactory<Program>> Host(params (string Key, string Value)[] settings) => new(() =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                foreach (var (key, value) in settings)
                    b.UseSetting(key, value);
            }));

        /// <summary>Bandera de módulos encendida, la de OT apagada.</summary>
        public WebApplicationFactory<Program> Modulos => _modulos.Value;

        /// <summary>Bandera OT encendida, la de módulos apagada.</summary>
        public WebApplicationFactory<Program> Ot => _ot.Value;

        /// <summary>Las dos banderas apagadas.</summary>
        public WebApplicationFactory<Program> Ninguno => _ninguno.Value;

        public void Dispose()
        {
            foreach (var host in new[] { _modulos, _ot, _ninguno })
                if (host.IsValueCreated)
                    host.Value.Dispose();
        }
    }

    private static IPermissionEnforcementState State(WebApplicationFactory<Program> f) =>
        f.Services.GetRequiredService<IPermissionEnforcementState>();

    [Theory]
    [InlineData("reportes.read")]
    [InlineData("reportes.uso.read")]
    [InlineData("reportes.programacion.manage")]
    public void Con_el_flag_de_modulos_apagado_los_slugs_de_modulo_no_se_exigen(string slug)
    {
        State(hosts.Ninguno).IsEffectivelyEnforced(slug).Should().BeFalse();
    }

    [Theory]
    [InlineData("reportes.read")]
    [InlineData("reportes.uso.read")]
    [InlineData("reportes.programacion.manage")]
    public void Con_el_flag_de_modulos_encendido_los_slugs_de_modulo_se_exigen(string slug)
    {
        State(hosts.Modulos).IsEffectivelyEnforced(slug).Should().BeTrue();
    }

    [Fact]
    public void Los_slugs_ot_dependen_del_flag_OT_y_no_del_de_modulos()
    {
        State(hosts.Modulos).IsEffectivelyEnforced(OtPermissionMap.Tramites).Should().BeFalse();
        State(hosts.Ot).IsEffectivelyEnforced(OtPermissionMap.Tramites).Should().BeTrue();
    }

    [Fact]
    public void Un_slug_sin_flag_siempre_se_exige()
    {
        State(hosts.Ninguno).IsEffectivelyEnforced("ict.pii.reveal").Should().BeTrue();
    }

    [Theory]
    [InlineData("matriculas.read")]
    [InlineData("matriculas.create")]
    [InlineData("traspasos.read")]
    [InlineData("traspasos.create")]
    [InlineData("otros-tramites.read")]
    [InlineData("otros-tramites.create")]
    public void Los_permisos_por_familia_siguen_el_flag_de_modulos(string slug)
    {
        State(hosts.Ninguno).IsEffectivelyEnforced(slug).Should().BeFalse();
        State(hosts.Modulos).IsEffectivelyEnforced(slug).Should().BeTrue();
    }
}
