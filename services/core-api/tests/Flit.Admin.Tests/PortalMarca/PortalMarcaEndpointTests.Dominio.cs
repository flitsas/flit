using System.Net;
using Flit.Admin.Domain.Companies.TransitOffices.PortalDominio;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>
/// HU #14381 AC5 — el alcance de <c>/api/v1/admin/ot/portal-dominio</c> por HTTP: un <c>ot_admin</c> que nombra a otro organismo, o a un
/// dominio (<c>id</c>) de otro organismo, recibe 403 en todas las rutas y no cambia nada; un rol ajeno no entra. Un organismo puede tener
/// varios dominios: el <c>GET</c> responde la lista (vacía con 200).
/// </summary>
public sealed partial class PortalMarcaEndpointTests
{
    private const string RutaDominio = "/api/v1/admin/ot/portal-dominio";
    private static readonly Guid DominioDeB = Guid.Parse("d0000000-0000-4000-8000-00000001437b");
    private static readonly Guid DominioDeA = Guid.Parse("d0000000-0000-4000-8000-00000001437a");
    private static readonly DateTimeOffset AhoraDominio = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private static DominioDePortal Dominio(Guid id, Guid organismo, string estado, bool principal = false) =>
        new(id, organismo, $"{estado}.{id.ToString()[^4..]}.example.test", estado, null, "TOKEN-DE-VERIFICACION-0123456789", null, null, null, null, null,
            null, 0, null, AhoraDominio, 1, principal);

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Dominio_OtAdminQueNombraAOtroOrganismo_Recibe403(string metodo)
    {
        using var peticion = new HttpRequestMessage(new HttpMethod(metodo), $"{RutaDominio}?transitOfficeId={OrganismoB}");
        if (metodo == "POST")
            peticion.Content = Json(new { host = "portal.ejemplo.test" });

        var r = await Cliente(OtAdmin(TenantA)).SendAsync(peticion, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.Dominios.DidNotReceiveWithAnyArgs().RegistrarAsync(default, default!, default!, default, default, Ct);
    }

    [Theory]
    [InlineData("POST", "/verificar")]
    [InlineData("PUT", "/principal")]
    [InlineData("DELETE", "")]
    public async Task Dominio_OtAdminQueNombraAOtroOrganismoEnUnaRutaConId_Recibe403(string metodo, string sub)
    {
        using var peticion = new HttpRequestMessage(new HttpMethod(metodo), $"{RutaDominio}/{DominioDeB}{sub}?transitOfficeId={OrganismoB}");

        var r = await Cliente(OtAdmin(TenantA)).SendAsync(peticion, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.Dominios.DidNotReceiveWithAnyArgs().RetirarAsync(default, default, Ct);
    }

    [Theory]
    [InlineData("POST", "/verificar")]
    [InlineData("PUT", "/principal")]
    [InlineData("DELETE", "")]
    public async Task Dominio_UnIdDeOtroOrganismoConElOrganismoPropio_Recibe403YNoCambiaNada(string metodo, string sub)
    {
        _factory.Dominios.ObtenerPorIdAsync(DominioDeB, Arg.Any<CancellationToken>()).Returns(Dominio(DominioDeB, OrganismoB, "active"));
        using var peticion = new HttpRequestMessage(new HttpMethod(metodo), $"{RutaDominio}/{DominioDeB}{sub}");

        var r = await Cliente(OtAdmin(TenantA)).SendAsync(peticion, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.Dominios.DidNotReceiveWithAnyArgs().RetirarAsync(default, default, Ct);
        await _factory.Dominios.DidNotReceiveWithAnyArgs().EstablecerPrincipalAsync(default, default, Ct);
        await _factory.Dominios.DidNotReceiveWithAnyArgs().AplicarComprobacionAsync(default, default!, default, default, default, default, default, default, default, default, Ct);
    }

    [Theory]
    [InlineData("POST", "/verificar")]
    [InlineData("PUT", "/principal")]
    [InlineData("DELETE", "")]
    public async Task Dominio_SuperAdminConElOrganismoAYElIdDeB_Recibe403(string metodo, string sub)
    {
        _factory.Dominios.ObtenerPorIdAsync(DominioDeB, Arg.Any<CancellationToken>()).Returns(Dominio(DominioDeB, OrganismoB, "active"));
        using var peticion = new HttpRequestMessage(new HttpMethod(metodo), $"{RutaDominio}/{DominioDeB}{sub}?transitOfficeId={OrganismoA}");

        var r = await Cliente(SuperAdmin()).SendAsync(peticion, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Dominio_SinDominios_ElGetResponde200ConListaVacia()
    {
        _factory.Dominios.ListarAsync(OrganismoA, Arg.Any<CancellationToken>()).Returns([]);

        var r = await Cliente(OtAdmin(TenantA)).GetAsync(RutaDominio, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Raiz(r)).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Dominio_ElGetListaTodosConSuIdPrincipalYEstado()
    {
        _factory.Dominios.ListarAsync(OrganismoA, Arg.Any<CancellationToken>()).Returns([
            Dominio(DominioDeA, OrganismoA, "active", principal: true),
            Dominio(Guid.Parse("d0000000-0000-4000-8000-0000000143aa"), OrganismoA, "pending")]);

        var r = await Cliente(OtAdmin(TenantA)).GetAsync(RutaDominio, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var lista = await Raiz(r);
        lista.GetArrayLength().Should().Be(2);
        lista[0].GetProperty("id").GetGuid().Should().Be(DominioDeA);
        lista[0].GetProperty("principal").GetBoolean().Should().BeTrue();
        lista[0].GetProperty("estado").GetString().Should().Be("activo");
        lista[1].GetProperty("estado").GetString().Should().Be("pendiente");
        lista[0].GetProperty("dns").EnumerateArray().Select(d => d.GetProperty("tipo").GetString()).Should().Contain("TXT");
    }

    [Fact]
    public async Task Dominio_PrincipalSobreUnoNoActivo_Responde409ConElMotivo()
    {
        _factory.Dominios.ObtenerPorIdAsync(DominioDeA, Arg.Any<CancellationToken>()).Returns(Dominio(DominioDeA, OrganismoA, "failed"));

        var r = await Cliente(OtAdmin(TenantA)).PutAsync($"{RutaDominio}/{DominioDeA}/principal", null, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Raiz(r)).GetProperty("code").GetString().Should().Be("PORTAL_DOMINIO_PRINCIPAL_NO_ACTIVO");
    }

    [Fact]
    public async Task Dominio_RetirarUnoPropio_Responde204SinCuerpo()
    {
        _factory.Dominios.ObtenerPorIdAsync(DominioDeA, Arg.Any<CancellationToken>()).Returns(Dominio(DominioDeA, OrganismoA, "active"));
        _factory.Dominios.RetirarAsync(DominioDeA, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(Dominio(DominioDeA, OrganismoA, "active"));

        var r = await Cliente(OtAdmin(TenantA)).DeleteAsync($"{RutaDominio}/{DominioDeA}", Ct);

        r.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await r.Content.ReadAsStringAsync(Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Dominio_SinSesion_Responde401()
    {
        var r = await _factory.CreateClient().GetAsync(RutaDominio, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
