using System.Net;
using System.Text.Json;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>Mejora de la Épica #14297 — contrato HTTP de la lista de banners (mismo grupo y permisos que logo, banner y carrusel).</summary>
public sealed partial class PortalMarcaEndpointTests
{
    [Fact]
    public async Task Banners_AgregarEditarOrdenarYQuitarDevuelvenElPortalMarcaCompletoConLaFormaDelContrato()
    {
        var cliente = Cliente(OtAdmin(TenantA));

        var r1 = await cliente.PostAsync($"{Ruta}/banners", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), "Primero", "https://example.test/a"), Ct);
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        var raiz1 = await Raiz(r1);
        var a = raiz1.GetProperty("banners")[0];
        a.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("imageId", "imageUrl", "alt", "linkUrl");
        var idA = a.GetProperty("imageId").GetGuid();
        a.GetProperty("imageUrl").GetString().Should().Be($"{Ruta}/imagenes/{idA}");
        a.GetProperty("alt").GetString().Should().Be("Primero");
        a.GetProperty("linkUrl").GetString().Should().Be("https://example.test/a");
        raiz1.GetProperty("bannerUrl").GetString().Should().Be(a.GetProperty("imageUrl").GetString(), "bannerUrl legacy = primer banner");
        (await cliente.GetAsync(a.GetProperty("imageUrl").GetString()!, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var r2 = await Raiz(await cliente.PostAsync($"{Ruta}/banners", MultipartCarrusel("b.jpg", ImagenesDePrueba.Jpeg(), "Segundo"), Ct));
        var idB = r2.GetProperty("banners")[1].GetProperty("imageId").GetGuid();
        r2.GetProperty("banners")[1].GetProperty("linkUrl").ValueKind.Should().Be(JsonValueKind.Null);
        r2.GetProperty("draft").GetProperty("banners").GetArrayLength().Should().Be(2);

        var editado = await Raiz(await cliente.PutAsync($"{Ruta}/banners/{idB}", Json(new { alt = "Editado", linkUrl = "http://example.test/b" }), Ct));
        editado.GetProperty("banners")[1].GetProperty("alt").GetString().Should().Be("Editado");

        var ordenado = await Raiz(await cliente.PutAsync($"{Ruta}/banners/orden", Json(new { imageIds = new[] { idB, idA } }), Ct));
        ordenado.GetProperty("banners").EnumerateArray().Select(i => i.GetProperty("imageId").GetGuid()).Should().Equal(idB, idA);

        var quitado = await Raiz(await cliente.DeleteAsync($"{Ruta}/banners/{idB}", Ct));
        quitado.GetProperty("banners").GetArrayLength().Should().Be(1);
        (await Raiz(await cliente.GetAsync(Ruta, Ct))).GetProperty("banners")[0].GetProperty("imageId").GetGuid().Should().Be(idA);
    }

    [Fact]
    public async Task Banners_ElSeptimoResponde409ConElCodigoBannersLleno()
    {
        var cliente = Cliente(OtAdmin(TenantA));
        for (var i = 0; i < 6; i++)
            (await cliente.PostAsync($"{Ruta}/banners", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), $"Banner {i}"), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var r = await cliente.PostAsync($"{Ruta}/banners", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), "Séptimo"), Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Raiz(r)).GetProperty("code").GetString().Should().Be("BANNERS_LLENO");
    }

    [Fact]
    public async Task Banners_ArchivoFaltanteOInvalidoYAltFaltanteRespondenConSuCodigo()
    {
        var cliente = Cliente(OtAdmin(TenantA));

        var sinArchivo = await cliente.PostAsync($"{Ruta}/banners", new MultipartFormDataContent { { new StringContent("Alt"), "alt" } }, Ct);
        var gif = await cliente.PostAsync($"{Ruta}/banners", MultipartCarrusel("a.gif", ImagenesDePrueba.Png(), "Alt"), Ct);
        var sinAlt = await cliente.PostAsync($"{Ruta}/banners", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), null), Ct);
        var malEnlace = await cliente.PostAsync($"{Ruta}/banners", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), "Alt", "javascript:alert(1)"), Ct);

        foreach (var r in new[] { sinArchivo, gif })
            (await Raiz(r)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.ArchivoInvalido);
        foreach (var r in new[] { sinAlt, malEnlace })
            (await Raiz(r)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.Datos);
        new[] { sinArchivo, gif, sinAlt, malEnlace }.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.BadRequest);
        _factory.Almacen.Archivos.Should().BeEmpty();
    }

    [Fact]
    public async Task Banners_EditarOQuitarUnoAjenoResponde404_Y_UnOrdenInvalidoResponde400()
    {
        var cliente = Cliente(OtAdmin(TenantA));

        var editar = await cliente.PutAsync($"{Ruta}/banners/{Guid.NewGuid()}", Json(new { alt = "x", linkUrl = (string?)null }), Ct);
        var quitar = await cliente.DeleteAsync($"{Ruta}/banners/{Guid.NewGuid()}", Ct);
        foreach (var r in new[] { editar, quitar })
        {
            r.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await Raiz(r)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.NoEncontrada);
        }

        var ajena = await cliente.PutAsync($"{Ruta}/banners/orden", Json(new { imageIds = new[] { Guid.NewGuid() } }), Ct);
        var noGuid = await cliente.PutAsync($"{Ruta}/banners/orden", Json(new { imageIds = new[] { "x" } }), Ct);
        foreach (var r in new[] { ajena, noGuid })
        {
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await Raiz(r)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.Datos);
        }
    }

    [Theory]
    [InlineData("POST", "/banners")]
    [InlineData("PUT", "/banners/orden")]
    [InlineData("PUT", "/banners/0199b7e0-0000-7000-8000-0000000000a1")]
    [InlineData("DELETE", "/banners/0199b7e0-0000-7000-8000-0000000000a1")]
    public async Task Banners_OtAdminDelOrganismoAQueNombraAlOrganismoB_403YNoCambiaNada(string metodo, string sub)
    {
        var cliente = Cliente(OtAdmin(TenantA));
        using var peticion = new HttpRequestMessage(new HttpMethod(metodo), $"{Ruta}{sub}?transitOfficeId={OrganismoB}");
        if (metodo == "POST")
            peticion.Content = MultipartCarrusel("a.png", ImagenesDePrueba.Png(), "Intruso");
        else if (metodo == "PUT")
            peticion.Content = Json(sub.EndsWith("orden", StringComparison.Ordinal) ? new { imageIds = Array.Empty<string>() } : new { alt = "x" });

        var r = await cliente.SendAsync(peticion, Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Raiz(r)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.Prohibido);
        (await _factory.Repo.ObtenerAsync(OrganismoB, Ct)).Draft.Banners.Should().BeEmpty();
        _factory.Almacen.Archivos.Should().BeEmpty();
    }

    [Fact]
    public async Task Banners_SinSesionOConOtroRolRespondeSinPermitirNada_Y_ElSuperAdminIndicaElOrganismo()
    {
        var anonimo = _factory.CreateClient();
        (await anonimo.PostAsync($"{Ruta}/banners", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), "x"), Ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        var otro = Cliente(Token(TenantA, "operator", "TRANSIT_OFFICE"));
        (await otro.PostAsync($"{Ruta}/banners", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), "x"), Ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        var sa = Cliente(SuperAdmin());
        (await sa.PostAsync($"{Ruta}/banners?transitOfficeId={OrganismoA}", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), "Desde SA"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _factory.Repo.ObtenerAsync(OrganismoA, Ct)).Draft.Banners.Should().ContainSingle().Which.Alt.Should().Be("Desde SA");
    }

    [Fact]
    public async Task Banners_ElEndpointLegacyDelBannerUnicoSigueFuncionandoSobreElPrimero()
    {
        var cliente = Cliente(OtAdmin(TenantA));
        var con = await Raiz(await cliente.PostAsync($"{Ruta}/banner", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), null), Ct));
        con.GetProperty("banners")[0].GetProperty("alt").GetString().Should().Be("Banner del organismo");
        con.GetProperty("bannerUrl").GetString().Should().NotBeNull();

        var sin = await Raiz(await cliente.DeleteAsync($"{Ruta}/banner", Ct));
        sin.GetProperty("banners").GetArrayLength().Should().Be(0);
        sin.GetProperty("bannerUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
