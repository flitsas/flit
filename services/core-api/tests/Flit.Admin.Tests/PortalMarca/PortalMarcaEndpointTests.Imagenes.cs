using System.Net;
using System.Text.Json;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>HU #14376 (Feature #14374) — contrato HTTP de logo, banner e imágenes de administración.</summary>
public sealed partial class PortalMarcaEndpointTests
{
    [Theory]
    [InlineData("POST", "/logo")]
    [InlineData("POST", "/banner")]
    [InlineData("DELETE", "/logo")]
    [InlineData("DELETE", "/banner")]
    public Task AC4_OtAdminDelOrganismoAQueNombraAlOrganismoB_403YNoCambiaNada_Imagenes(string metodo, string sub) => Verificar403Async(metodo, sub);

    [Fact]
    public async Task AC4_OtAdminNoVeLaImagenDeOtroOrganismo_403_YSiVeLaSuya()
    {
        var ajena = await SembrarImagenAsync(OrganismoB);
        var propia = await SembrarImagenAsync(OrganismoA);

        (await Cliente(OtAdmin(TenantA)).GetAsync($"{Ruta}/imagenes/{ajena}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var ok = await Cliente(OtAdmin(TenantA)).GetAsync($"{Ruta}/imagenes/{propia}", Ct);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        ok.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        ok.Headers.CacheControl!.Private.Should().BeTrue("la imagen de administración, también de borrador, nunca es pública");
    }

    [Fact]
    public async Task AC4_UnaImagenInexistenteResponde404()
    {
        (await Cliente(OtAdmin(TenantA)).GetAsync($"{Ruta}/imagenes/{Guid.NewGuid()}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AC1_ElLogoValidoSeCargaYQuedaEnElBorradorConSuUrlYSePuedeQuitar()
    {
        var cliente = Cliente(OtAdmin(TenantA));

        var r = await cliente.PostAsync($"{Ruta}/logo", Multipart("logo.png", ImagenesDePrueba.Png()), Ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await Raiz(r);
        var id = cuerpo.GetProperty("draft").GetProperty("logoId").GetGuid();
        cuerpo.GetProperty("logoUrl").GetString().Should().Be($"{Ruta}/imagenes/{id}");
        (await cliente.GetAsync(cuerpo.GetProperty("logoUrl").GetString()!, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var quitado = await Raiz(await cliente.DeleteAsync($"{Ruta}/logo", Ct));
        quitado.GetProperty("draft").GetProperty("logoId").ValueKind.Should().Be(JsonValueKind.Null);
        quitado.GetProperty("logoUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task AC2_AC3_UnArchivoInvalidoOQueMienteEnSuExtensionResponde400ConElCodigoYNoSeGuarda()
    {
        var cliente = Cliente(OtAdmin(TenantA));

        var mentira = await cliente.PostAsync($"{Ruta}/banner", Multipart("banner.png", ImagenesDePrueba.Jpeg()), Ct);
        var gif = await cliente.PostAsync($"{Ruta}/logo", Multipart("logo.gif", ImagenesDePrueba.Png()), Ct);
        var grande = await cliente.PostAsync($"{Ruta}/logo", Multipart("logo.png", ImagenesDePrueba.Png(600 * 1024)), Ct);
        var sinArchivo = await cliente.PostAsync($"{Ruta}/logo", new MultipartFormDataContent { { new StringContent("x"), "otro" } }, Ct);

        foreach (var r in new[] { mentira, gif, grande, sinArchivo })
        {
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await Raiz(r)).GetProperty("code").GetString().Should().Be(PortalMarcaErrores.ArchivoInvalido);
        }

        (await Raiz(grande)).GetProperty("message").GetString().Should().Contain("512 KB");
        _factory.Almacen.Archivos.Should().BeEmpty();
        (await _factory.Repo.ObtenerAsync(OrganismoA, Ct)).Draft.LogoId.Should().BeNull();
    }
}
