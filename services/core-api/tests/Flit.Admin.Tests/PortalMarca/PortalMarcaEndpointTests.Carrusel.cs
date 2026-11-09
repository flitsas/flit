using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>Mejora de la Épica #14297 — contrato HTTP del carrusel de la marca del portal (mismo grupo y permisos que logo y banner).</summary>
public sealed partial class PortalMarcaEndpointTests
{
    private static MultipartFormDataContent MultipartCarrusel(string nombre, byte[] contenido, string? alt, string? enlace = null)
    {
        var form = new MultipartFormDataContent();
        var parte = new ByteArrayContent(contenido);
        parte.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(parte, "file", nombre);
        if (alt is not null)
            form.Add(new StringContent(alt), "alt");
        if (enlace is not null)
            form.Add(new StringContent(enlace), "linkUrl");
        return form;
    }

    [Fact]
    public async Task Carrusel_AgregarEditarOrdenarYQuitarDevuelvenElPortalMarcaCompletoConLaFormaDelContrato()
    {
        var cliente = Cliente(OtAdmin(TenantA));

        var r1 = await cliente.PostAsync($"{Ruta}/carrusel", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), "Primera", "https://example.test/a"), Ct);
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        var a = (await Raiz(r1)).GetProperty("carousel")[0];
        a.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("imageId", "imageUrl", "alt", "linkUrl");
        var idA = a.GetProperty("imageId").GetGuid();
        a.GetProperty("imageUrl").GetString().Should().Be($"{Ruta}/imagenes/{idA}");
        a.GetProperty("alt").GetString().Should().Be("Primera");
        a.GetProperty("linkUrl").GetString().Should().Be("https://example.test/a");
        (await cliente.GetAsync(a.GetProperty("imageUrl").GetString()!, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var r2 = await Raiz(await cliente.PostAsync($"{Ruta}/carrusel", MultipartCarrusel("b.jpg", ImagenesDePrueba.Jpeg(), "Segunda"), Ct));
        var idB = r2.GetProperty("carousel")[1].GetProperty("imageId").GetGuid();
        r2.GetProperty("carousel")[1].GetProperty("linkUrl").ValueKind.Should().Be(JsonValueKind.Null);
        r2.GetProperty("hasUnpublishedChanges").GetBoolean().Should().BeTrue();

        var editado = await Raiz(await cliente.PutAsync($"{Ruta}/carrusel/{idB}", Json(new { alt = "Editada", linkUrl = "http://example.test/b" }), Ct));
        editado.GetProperty("carousel")[1].GetProperty("alt").GetString().Should().Be("Editada");
        editado.GetProperty("carousel")[1].GetProperty("linkUrl").GetString().Should().Be("http://example.test/b");

        var ordenado = await Raiz(await cliente.PutAsync($"{Ruta}/carrusel/orden", Json(new { imageIds = new[] { idB, idA } }), Ct));
        ordenado.GetProperty("carousel").EnumerateArray().Select(i => i.GetProperty("imageId").GetGuid()).Should().Equal(idB, idA);
        ordenado.GetProperty("draft").GetProperty("carousel")[0].GetProperty("imageId").GetGuid().Should().Be(idB);

        var quitado = await Raiz(await cliente.DeleteAsync($"{Ruta}/carrusel/{idB}", Ct));
        quitado.GetProperty("carousel").GetArrayLength().Should().Be(1);
        (await Raiz(await cliente.GetAsync(Ruta, Ct))).GetProperty("carousel")[0].GetProperty("imageId").GetGuid().Should().Be(idA);
    }

    [Fact]
    public async Task Carrusel_LaSeptimaImagenResponde409ConElCodigoCarruselLleno()
    {
        var cliente = Cliente(OtAdmin(TenantA));
        for (var i = 0; i < 6; i++)
            (await cliente.PostAsync($"{Ruta}/carrusel", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), $"Imagen {i}"), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var r = await cliente.PostAsync($"{Ruta}/carrusel", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), "Séptima"), Ct);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Raiz(r)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.CarruselLleno);
    }

    [Fact]
    public async Task Carrusel_ArchivoFaltanteOInvalidoYAltFaltanteRespondenConSuCodigo()
    {
        var cliente = Cliente(OtAdmin(TenantA));

        var sinArchivo = await cliente.PostAsync($"{Ruta}/carrusel", new MultipartFormDataContent { { new StringContent("Alt"), "alt" } }, Ct);
        var gif = await cliente.PostAsync($"{Ruta}/carrusel", MultipartCarrusel("a.gif", ImagenesDePrueba.Png(), "Alt"), Ct);
        var grande = await cliente.PostAsync($"{Ruta}/carrusel", MultipartCarrusel("a.png", ImagenesDePrueba.Png(600 * 1024), "Alt"), Ct);
        var sinAlt = await cliente.PostAsync($"{Ruta}/carrusel", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), null), Ct);
        var malEnlace = await cliente.PostAsync($"{Ruta}/carrusel", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), "Alt", "javascript:alert(1)"), Ct);

        foreach (var r in new[] { sinArchivo, gif, grande })
            (await Raiz(r)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.ArchivoInvalido);
        foreach (var r in new[] { sinAlt, malEnlace })
            (await Raiz(r)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.Datos);
        new[] { sinArchivo, gif, grande, sinAlt, malEnlace }.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.BadRequest);
        _factory.Almacen.Archivos.Should().BeEmpty();
    }

    [Fact]
    public async Task Carrusel_UnOrdenQueNoEsPermutacionExactaRespondePortalMarcaDatosInvalidos()
    {
        var cliente = Cliente(OtAdmin(TenantA));
        var uno = await Raiz(await cliente.PostAsync($"{Ruta}/carrusel", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), "Uno"), Ct));
        var id = uno.GetProperty("carousel")[0].GetProperty("imageId").GetGuid();

        var ajena = await cliente.PutAsync($"{Ruta}/carrusel/orden", Json(new { imageIds = new[] { Guid.NewGuid() } }), Ct);
        var repetida = await cliente.PutAsync($"{Ruta}/carrusel/orden", Json(new { imageIds = new[] { id, id } }), Ct);
        var noGuid = await cliente.PutAsync($"{Ruta}/carrusel/orden", Json(new { imageIds = new[] { "x" } }), Ct);

        foreach (var r in new[] { ajena, repetida, noGuid })
        {
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await Raiz(r)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.Datos);
        }
    }

    [Fact]
    public async Task Carrusel_EditarOQuitarUnaImagenAjenaResponde404()
    {
        var cliente = Cliente(OtAdmin(TenantA));

        var editar = await cliente.PutAsync($"{Ruta}/carrusel/{Guid.NewGuid()}", Json(new { alt = "x", linkUrl = (string?)null }), Ct);
        var quitar = await cliente.DeleteAsync($"{Ruta}/carrusel/{Guid.NewGuid()}", Ct);

        foreach (var r in new[] { editar, quitar })
        {
            r.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await Raiz(r)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.NoEncontrada);
        }
    }

    [Theory]
    [InlineData("POST", "/carrusel")]
    [InlineData("PUT", "/carrusel/orden")]
    [InlineData("PUT", "/carrusel/0199b7e0-0000-7000-8000-0000000000a1")]
    [InlineData("DELETE", "/carrusel/0199b7e0-0000-7000-8000-0000000000a1")]
    public async Task Carrusel_OtAdminDelOrganismoAQueNombraAlOrganismoB_403YNoCambiaNada(string metodo, string sub)
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
        (await _factory.Repo.ObtenerAsync(OrganismoB, Ct)).Draft.Carousel.Should().BeEmpty();
        _factory.Almacen.Archivos.Should().BeEmpty();
    }

    [Fact]
    public async Task Carrusel_SinSesionOConOtroRolRespondeSinPermitirNada()
    {
        var anonimo = _factory.CreateClient();
        (await anonimo.PostAsync($"{Ruta}/carrusel", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), "x"), Ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        var otro = Cliente(Token(TenantA, "operator", "TRANSIT_OFFICE"));
        (await otro.PostAsync($"{Ruta}/carrusel", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), "x"), Ct)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Carrusel_ElSuperAdminIndicaElOrganismoConTransitOfficeId()
    {
        var cliente = Cliente(SuperAdmin());

        var r = await cliente.PostAsync($"{Ruta}/carrusel?transitOfficeId={OrganismoA}", MultipartCarrusel("a.png", ImagenesDePrueba.Png(), "Desde SA"), Ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _factory.Repo.ObtenerAsync(OrganismoA, Ct)).Draft.Carousel.Should().ContainSingle().Which.Alt.Should().Be("Desde SA");
    }
}
