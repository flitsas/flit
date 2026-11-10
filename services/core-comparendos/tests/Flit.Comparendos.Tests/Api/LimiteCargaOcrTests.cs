using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Plataforma;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>La API sobre Kestrel real: el TestServer no aplica límites de tamaño del cuerpo.</summary>
public sealed class ApiConKestrel : ApiConFuente
{
    protected override bool ConKestrel => true;
}

/// <summary>
/// GDC punto 10 — la carga de un paquete OCR admite 100 MB de archivos (+1 MB de sobre multipart, igual que el gateway y
/// el proxy de Next). Por encima responde 413 con un código y un mensaje que la pantalla puede mostrar.
/// </summary>
public sealed class LimiteCargaOcrTests(ApiConKestrel api) : IClassFixture<ApiConKestrel>
{
    private const int Mb = 1024 * 1024;

    private static MultipartFormDataContent Paquete(int archivos, int bytesPorArchivo)
    {
        var form = new MultipartFormDataContent { { new StringContent("Lote grande"), "nombre" } };
        for (var i = 0; i < archivos; i++)
            form.Add(new ByteArrayContent(new byte[bytesPorArchivo]), "archivos", $"doc{i}.pdf");
        return form;
    }

    private HttpClient Operador()
    {
        var cliente = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [Permisos.OcrVer, Permisos.OcrGestionar]));
        // Con 100-continue el cliente espera la respuesta antes de mandar el cuerpo: así lee el 413 en vez de chocar con
        // la conexión que Kestrel cierra mientras el cuerpo sigue llegando (el navegador sí lee la respuesta).
        cliente.DefaultRequestHeaders.ExpectContinue = true;
        return cliente;
    }

    [Fact]
    public async Task Paquete_De100Mb_LlegaALaApi()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;

        // 5 × 20 MB: por encima de los 30.000.000 bytes por defecto de Kestrel. La API lee el formulario y responde por
        // la regla de negocio que toque (aquí la empresa no tiene OCR); lo que importa es que no es el corte de tamaño.
        var r = await Operador().PostAsync("/api/v1/comparendos/ocr/paquetes", Paquete(5, 20 * Mb), ct);

        r.StatusCode.Should().NotBe(HttpStatusCode.RequestEntityTooLarge, await r.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task Paquete_DeMasDe100Mb_Da413ConMensaje()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;

        var r = await Operador().PostAsync("/api/v1/comparendos/ocr/paquetes", Paquete(6, 20 * Mb), ct);

        r.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        var cuerpo = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("code").GetString().Should().Be("PAQUETE_DEMASIADO_GRANDE");
        cuerpo.GetProperty("error").GetString().Should().Contain("dos paquetes");
    }
}
