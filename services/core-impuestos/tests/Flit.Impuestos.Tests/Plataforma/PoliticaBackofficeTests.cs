using System.Net;
using System.Security.Claims;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Credenciales;
using Flit.Impuestos.Tests.Usuarios;
using FluentAssertions;
using Xunit;

namespace Flit.Impuestos.Tests.Plataforma;

/// <summary>
/// HU #14694 (M13, plan §5.2, F5b-CF1) — <see cref="PoliticaBackoffice"/>: el Super Admin pasa desde cualquier compañía; un
/// usuario de la compañía FLIT con <c>impuestos.backoffice.ver</c> armado a mano también; nadie más (ni un Administrador Impuestos
/// de otra compañía con el mismo permiso, ni un token de servicio, ni un anónimo). En HTTP, <c>GET /backoffice/empresas</c> sigue
/// la misma regla.
/// </summary>
/// <example>
/// <code>
/// PoliticaBackoffice.Cumple(principal).Should().BeTrue();
/// </code>
/// </example>
public sealed class PoliticaBackofficeTests(ApiUsuariosFixture fixture) : IClassFixture<ApiUsuariosFixture>
{
    private ApiDePrueba Api => fixture.Api;

    [Fact]
    public void El_Super_Admin_pasa_sin_rol_de_producto()
    {
        PoliticaBackoffice.Cumple(Principal(("sub", Guid.NewGuid().ToString()), ("role_code", "SuperAdmin"))).Should().BeTrue();
    }

    [Fact]
    public void Un_usuario_de_FLIT_con_el_permiso_pasa_y_sin_el_no()
    {
        PoliticaBackoffice.Cumple(Principal(("sub", Guid.NewGuid().ToString()), ("tenant_type", "FLIT"), ("permissions", Permisos.BackofficeVer))).Should().BeTrue();
        PoliticaBackoffice.Cumple(Principal(("sub", Guid.NewGuid().ToString()), ("tenant_type", "FLIT"), ("permissions", Permisos.UsuariosGestionar))).Should().BeFalse();
    }

    [Theory]
    [InlineData("COMPANY")]
    [InlineData(null)]
    public void Fuera_de_FLIT_el_permiso_no_basta(string? tipo)
    {
        var claims = new List<(string, string)> { ("sub", Guid.NewGuid().ToString()), ("permissions", Permisos.BackofficeVer) };
        if (tipo is not null)
            claims.Add(("tenant_type", tipo));
        PoliticaBackoffice.Cumple(Principal([.. claims])).Should().BeFalse();
    }

    [Fact]
    public void Un_token_de_servicio_o_un_anonimo_no_pasan()
    {
        PoliticaBackoffice.Cumple(Principal(("sub", "svc-impuestos"), ("role_code", "SuperAdmin"))).Should().BeFalse();
        PoliticaBackoffice.Cumple(new ClaimsPrincipal(new ClaimsIdentity())).Should().BeFalse();
    }

    [Fact]
    public async Task En_HTTP_el_backoffice_es_del_Super_Admin_y_de_FLIT_con_permiso()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        var ruta = SuspensionConservaDatosTests.RutaEmpresas;

        using var sa = await Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api)).GetAsync(ruta, ct);
        using var flit = await Api.Cliente(Api.Token(tipoEmpresa: "FLIT", roles: ["soporte_flit"], permisos: [Permisos.BackofficeVer])).GetAsync(ruta, ct);
        using var admin = await Api.Cliente(Api.Token(roles: ["admin_impuestos"], permisos: [Permisos.BackofficeVer, Permisos.UsuariosGestionar])).GetAsync(ruta, ct);
        using var gestor = await Api.Cliente(UsuariosEndpointsTests.TokenGestor(Api, Guid.NewGuid())).GetAsync($"{ruta}/{Guid.NewGuid()}/aprovisionamiento", ct);
        using var anonimo = await Api.Anonimo().GetAsync(ruta, ct);

        sa.StatusCode.Should().Be(HttpStatusCode.OK);
        flit.StatusCode.Should().Be(HttpStatusCode.OK);
        admin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        gestor.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonimo.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static ClaimsPrincipal Principal(params (string Tipo, string Valor)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Tipo, c.Valor)), "Bearer"));
}
