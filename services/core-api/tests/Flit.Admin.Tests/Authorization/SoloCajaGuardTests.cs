using System.Security.Claims;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #14454 (Épica #14210, ADR-0082) — segunda capa del guardián solo-caja: un token cuyos roles son solo <c>ot_caja</c> no usa la API
/// de FLIT 2.0 (salvo la caja, su perfil y sesión y lo mínimo del shell), y cualquier otro usuario pasa exactamente como antes.
/// </summary>
public sealed class SoloCajaGuardTests
{
    private static ClaimsPrincipal User(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(AdminAuthorization.RoleClaimType, r)), "TestAuth"));

    private static async Task<(bool Llego, int Status, string Cuerpo)> InvocarAsync(ClaimsPrincipal user, string path, string method = "GET")
    {
        var llego = false;
        var context = new DefaultHttpContext { User = user };
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        var middleware = new SoloCajaGuardMiddleware(_ =>
        {
            llego = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        return (llego, context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Theory]
    [InlineData("/api/v1/admin/ot/users")]
    [InlineData("/api/v1/tramites/instances")]
    [InlineData("/api/v1/admin/ot/portal-caja/users")]
    public async Task TokenSoloCaja_EnUnaRutaDeLaApi_403CajaSinAcceso(string path)
    {
        var (llego, status, cuerpo) = await InvocarAsync(User(AdminAuthorization.OtCajaRole), path);

        llego.Should().BeFalse();
        status.Should().Be(StatusCodes.Status403Forbidden);
        cuerpo.Should().Contain("caja_sin_acceso");
    }

    [Theory]
    [InlineData("/api/v1/ot/caja/tramites/PN-2026-000101")]
    [InlineData("/api/v1/ot/caja/tramites/PN-2026-000101/confirmar")]
    [InlineData("/api/v1/ot/caja/cobros")]
    [InlineData("/api/v1/me")]
    [InlineData("/api/v1/me/branding")]
    [InlineData("/api/v1/me/ui-preferences")]
    [InlineData("/api/v1/auth/session-policy")]
    [InlineData("/api/v1/auth/change-password")]
    [InlineData("/api/v1/public/banners/active")]
    [InlineData("/api/v1/company/branding")]
    public async Task TokenSoloCaja_PuedeUsarLaCajaSuPerfilLaSesionYLoMinimoDelShell(string path)
    {
        var (llego, status, _) = await InvocarAsync(User(AdminAuthorization.OtCajaRole), path);

        llego.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData("/api/v1/ot/cajas")]
    [InlineData("/api/v1/ot/caja-otra")]
    [InlineData("/api/v1/mexico")]
    [InlineData("/api/v1/authors")]
    [InlineData("/api/v1/auth/admin/reset-password")]
    [InlineData("/api/v1/admin/ot/portal-caja/cobros")]
    [InlineData("/api/v1/ot/clients")]
    [InlineData("/api/v1/platform/me")]
    public async Task TokenSoloCaja_LasRutasParecidasPeroDistintas_403CajaSinAcceso(string path)
    {
        var (llego, status, cuerpo) = await InvocarAsync(User(AdminAuthorization.OtCajaRole), path);

        llego.Should().BeFalse();
        status.Should().Be(StatusCodes.Status403Forbidden);
        cuerpo.Should().Contain("caja_sin_acceso");
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task TokenSoloCaja_LaMarcaDeLaCompaniaEsDeSoloLectura(string method)
    {
        var (llego, status, _) = await InvocarAsync(User(AdminAuthorization.OtCajaRole), "/api/v1/company/branding", method);

        llego.Should().BeFalse();
        status.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task TokenSoloCaja_PuedeListarSusApps()
    {
        var (llego, status, _) = await InvocarAsync(User(AdminAuthorization.OtCajaRole), "/api/v1/platform/me/apps");

        llego.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData("ot_admin")]
    [InlineData("gestor_ot")]
    [InlineData("SuperAdmin")]
    public async Task OtroUsuarioDelOrganismo_PasaComoAntes(string rol)
    {
        var (llego, status, _) = await InvocarAsync(User(rol), "/api/v1/admin/ot/users");

        llego.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task CajaMasOtroRol_NoEsSoloCaja_PasaComoAntes()
    {
        var (llego, _, _) = await InvocarAsync(User(AdminAuthorization.OtCajaRole, "gestor_ot"), "/api/v1/admin/ot/users");

        llego.Should().BeTrue();
    }

    [Fact]
    public async Task SinRoles_NoEsSoloCaja_PasaComoAntes()
    {
        var (llego, _, _) = await InvocarAsync(User(), "/api/v1/admin/ot/users");

        llego.Should().BeTrue();
    }

    [Fact]
    public async Task PeticionAnonima_NoLaToca()
    {
        var (llego, _, _) = await InvocarAsync(new ClaimsPrincipal(new ClaimsIdentity()), "/api/v1/auth/login");

        llego.Should().BeTrue();
    }

    [Fact]
    public async Task FueraDeLaApi_NoLaToca()
    {
        var (llego, _, _) = await InvocarAsync(User(AdminAuthorization.OtCajaRole), "/health");

        llego.Should().BeTrue();
    }
}
