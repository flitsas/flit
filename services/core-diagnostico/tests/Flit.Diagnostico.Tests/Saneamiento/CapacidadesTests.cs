using System.Security.Claims;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// Capacidades de sesión del saneamiento (plan §5.3, S13; HU #14576 AC32; SP-8 con la decisión E3): <c>sanear</c> sigue la regla
/// general del permiso <c>saneamiento.solicitar</c> (el SuperAdmin pasa); <c>gestionarSaneamiento</c> exige la operación FLIT y el
/// permiso de gestión, así que una compañía cliente nunca la tiene. Sin base: el usuario sale de los claims.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Saneamiento.CapacidadesTests
/// </code>
/// </example>
public sealed class CapacidadesTests
{
    [Fact]
    public void HU14576_AC32_con_solicitar_la_capacidad_sanear_es_verdadera()
    {
        var capacidades = Capacidades.De(Usuario(roles: ["usuario_diagnostico"], permisos: [Permisos.SaneamientoSolicitar]));

        capacidades.Sanear.Should().BeTrue();
        capacidades.GestionarSaneamiento.Should().BeFalse();
        Capacidades.De(Usuario(roles: ["usuario_diagnostico"], permisos: [Permisos.DiagnosticosVer])).Sanear.Should().BeFalse();
    }

    [Fact]
    public void HU14576_AC32_operacion_FLIT_con_gestionar_tiene_gestionarSaneamiento()
    {
        var capacidades = Capacidades.De(Usuario(roles: ["operador"], permisos: [Permisos.BackofficeVer, Permisos.SaneamientoGestionar], tipoEmpresa: "FLIT"));

        capacidades.GestionarSaneamiento.Should().BeTrue();
    }

    // AC32 / SP (nota final de security-previa) — compañía cliente con los dos claims de backoffice armados a mano: falso.
    [Theory]
    [InlineData("CLIENTE")]
    [InlineData(null)]
    public void HU14576_AC32_compania_cliente_nunca_tiene_gestionarSaneamiento(string? tipoEmpresa)
    {
        var capacidades = Capacidades.De(Usuario(roles: ["admin_diagnostico"], permisos: [Permisos.BackofficeVer, Permisos.SaneamientoGestionar, Permisos.SaneamientoSolicitar], tipoEmpresa: tipoEmpresa));

        capacidades.GestionarSaneamiento.Should().BeFalse();
        capacidades.Sanear.Should().BeTrue();
    }

    // SP-8 con la decisión E3 — el SuperAdmin sí marca: las dos capacidades le salen verdaderas.
    [Fact]
    public void SP8_E3_el_SuperAdmin_tiene_sanear_y_gestionarSaneamiento()
    {
        var capacidades = Capacidades.De(Usuario(roles: ["SuperAdmin"], permisos: []));

        capacidades.Sanear.Should().BeTrue();
        capacidades.GestionarSaneamiento.Should().BeTrue();
    }

    private static UsuarioActual Usuario(string[] roles, string[] permisos, string? tipoEmpresa = null)
    {
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()) };
        claims.AddRange(roles.Select(r => new Claim("role_code", r)));
        claims.AddRange(permisos.Select(p => new Claim("permissions", p)));
        if (tipoEmpresa is not null)
            claims.Add(new Claim("tenant_type", tipoEmpresa));
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba")) } };
        return new UsuarioActual(http, new EmpresaFija(Guid.NewGuid()));
    }
}
