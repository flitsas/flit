using System.Net;
using System.Text.Json;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>
/// HU #14401 (Feature #14400) — contrato HTTP de <c>/api/v1/admin/plataforma/persona-natural/marca-web</c>: solo SuperAdmin (cualquier
/// otro rol, 403, y nada cambia), misma forma de la marca del organismo, sin banner, sin cruce con la marca de un organismo.
/// </summary>
public sealed partial class PortalMarcaEndpointTests
{
    private const string RutaWeb = "/api/v1/admin/plataforma/persona-natural/marca-web";

    [Theory]
    [InlineData("ot_admin", "TRANSIT_OFFICE")]
    [InlineData("ot_operador", null)]
    [InlineData("AdminCompany", null)]
    [InlineData("Radicador", null)]
    public async Task Web_CualquierRolQueNoSeaSuperAdminRecibe403EnTodasLasRutasYNadaCambia(string rol, string? tipo)
    {
        var cliente = Cliente(Token(TenantA, rol, tipo));
        var cuerpoMarca = Json(new { platformName = "Intruso", colors = new { primary = "#0B3D91", secondary = "#0F5132", onPrimary = "#FFFFFF" }, rowVersion = 0 });

        var respuestas = new[]
        {
            await cliente.GetAsync(RutaWeb, Ct),
            await cliente.PutAsync(RutaWeb, cuerpoMarca, Ct),
            await cliente.PostAsync($"{RutaWeb}/publish", Json(new { rowVersion = 0 }), Ct),
            await cliente.PostAsync($"{RutaWeb}/retire", Json(new { rowVersion = 0 }), Ct),
            await cliente.PostAsync($"{RutaWeb}/logo", Multipart("logo.png", ImagenesDePrueba.Png()), Ct),
            await cliente.DeleteAsync($"{RutaWeb}/logo", Ct),
            await cliente.GetAsync($"{RutaWeb}/imagenes/{Guid.NewGuid()}", Ct),
        };

        respuestas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Forbidden);
        (await _factory.Repo.ObtenerAsync(PortalMarcaAmbito.Web, Ct)).Should().Be(PortalMarcaRegistro.Vacio(PortalMarcaAmbito.Web));
        _factory.Auditoria.Entradas.Should().BeEmpty();
        _factory.Almacen.Archivos.Should().BeEmpty();
    }

    [Fact]
    public async Task Web_SinSesionResponde401()
    {
        (await _factory.CreateClient().GetAsync(RutaWeb, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Web_NoExponeBannerNiLoAceptaPorNingunaRuta()
    {
        var cliente = Cliente(SuperAdmin());

        var subida = await cliente.PostAsync($"{RutaWeb}/banner", Multipart("banner.png", ImagenesDePrueba.Png()), Ct);
        var baja = await cliente.DeleteAsync($"{RutaWeb}/banner", Ct);

        subida.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        baja.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        _factory.Almacen.Archivos.Should().BeEmpty();
        (await Raiz(await cliente.GetAsync(RutaWeb, Ct))).GetProperty("bannerUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Web_SuperAdminGuardaPublicaYRetiraSinIndicarOrganismo()
    {
        var cliente = Cliente(SuperAdmin());
        var vacia = await Raiz(await cliente.GetAsync(RutaWeb, Ct));
        vacia.GetProperty("published").ValueKind.Should().Be(JsonValueKind.Null);
        vacia.GetProperty("rowVersion").GetInt64().Should().Be(0);

        var guardada = await cliente.PutAsync(RutaWeb, Json(ColoresValidos(0)), Ct);
        guardada.StatusCode.Should().Be(HttpStatusCode.OK);
        var version = (await Raiz(guardada)).GetProperty("rowVersion").GetInt64();

        var publicada = await cliente.PostAsync($"{RutaWeb}/publish", Json(new { rowVersion = version }), Ct);
        publicada.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await Raiz(publicada);
        cuerpo.GetProperty("publishedVersion").GetInt32().Should().Be(1);

        var retirada = await cliente.PostAsync($"{RutaWeb}/retire", Json(new { rowVersion = cuerpo.GetProperty("rowVersion").GetInt64() }), Ct);
        retirada.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Raiz(retirada)).GetProperty("published").ValueKind.Should().Be(JsonValueKind.Null);
        _factory.Auditoria.Entradas.Select(a => a.EntityName).Should()
            .Equal("portal_marca_web_borrador", "portal_marca_web_publicada", "portal_marca_web_retirada");
    }

    [Fact]
    public async Task Web_ConContrasteMenorA45NoSePublicaYRespondeElMotivo()
    {
        var cliente = Cliente(SuperAdmin());
        var malo = new { platformName = "Web", colors = new { primary = "#FFEB3B", secondary = "#0F5132", onPrimary = "#FFFFFF" }, rowVersion = 0 };
        var version = (await Raiz(await cliente.PutAsync(RutaWeb, Json(malo), Ct))).GetProperty("rowVersion").GetInt64();

        var r = await cliente.PostAsync($"{RutaWeb}/publish", Json(new { rowVersion = version }), Ct);

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Raiz(r)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.Contraste);
    }

    [Fact]
    public async Task Web_ElLogoSeCargaPorLaRutaDeLaWebYUnaImagenDeOrganismoNoSeLeePorEllaNiAlReves()
    {
        var cliente = Cliente(SuperAdmin());
        var subida = await Raiz(await cliente.PostAsync($"{RutaWeb}/logo", Multipart("logo.png", ImagenesDePrueba.Png()), Ct));
        var id = subida.GetProperty("draft").GetProperty("logoId").GetGuid();
        subida.GetProperty("logoUrl").GetString().Should().Be($"{RutaWeb}/imagenes/{id}");
        var delOrganismo = await SembrarImagenAsync(OrganismoA);

        (await cliente.GetAsync($"{RutaWeb}/imagenes/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await cliente.GetAsync($"{RutaWeb}/imagenes/{delOrganismo}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Cliente(OtAdmin(TenantA)).GetAsync($"{Ruta}/imagenes/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "un ot_admin no ve la imagen de la web por la ruta del organismo");
    }

    [Fact]
    public async Task Web_ElSuperAdminGuardarLaWebNoCambiaLaMarcaDelOrganismoYViceversa()
    {
        var cliente = Cliente(SuperAdmin());
        var organismoAntes = await _factory.Repo.ObtenerAsync(OrganismoA, Ct);
        await cliente.PutAsync(RutaWeb, Json(ColoresValidos(0)), Ct);
        (await _factory.Repo.ObtenerAsync(OrganismoA, Ct)).Should().Be(organismoAntes);

        var webAntes = await _factory.Repo.ObtenerAsync(PortalMarcaAmbito.Web, Ct);
        var porOrganismo = await cliente.PutAsync($"{Ruta}?transitOfficeId={OrganismoA}", Json(ColoresValidos(0)), Ct);
        porOrganismo.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _factory.Repo.ObtenerAsync(PortalMarcaAmbito.Web, Ct)).Should().Be(webAntes);
        (await _factory.Repo.ObtenerAsync(OrganismoA, Ct)).Draft.PlatformName.Should().NotBeNull();
    }
}
