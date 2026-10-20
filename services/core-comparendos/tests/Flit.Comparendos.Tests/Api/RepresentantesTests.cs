using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Configuracion;
using Flit.Comparendos.Api.Plataforma;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13475 — representantes legales y permisos por OT.</summary>
public sealed class RepresentantesTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    private static readonly string[] Gestion = [Permisos.RepresentantesVer, Permisos.RepresentantesGestionar, Permisos.MonitoreoGestionar, Permisos.OtsGestionar];

    private static object Rl(string alcance, Guid? nit = null, string documento = "79123456", bool activo = true) => new
    {
        alcance, identificacionMonitoreadaId = nit, tipoDocumento = "CC", numeroDocumento = documento, nombres = $"Representante {documento}",
        cargo = "Representante legal", correo = "rl@empresa.test", permisoEnvioDp = true, permisoCambioContraventor = true, activo,
    };

    [Fact]
    public async Task AC1_ElDelNitGanaSobreElDeLaEmpresa()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Gestion));
        var nit = (await (await cliente.PostAsJsonAsync("/api/v1/comparendos/monitoreo/nits", new { numero = "900123456" }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        (await cliente.PostAsJsonAsync("/api/v1/comparendos/representantes", Rl("Empresa"), ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        var delNit = await (await cliente.PostAsJsonAsync("/api/v1/comparendos/representantes", Rl("Identificacion", nit, "80111222"), ct)).Content.ReadFromJsonAsync<JsonElement>(ct);

        await using var scope = api.App.Services.CreateAsyncScope();
        var resolutor = scope.ServiceProvider.GetRequiredService<ResolutorRepresentante>();
        (await resolutor.ResolverAsync(empresa, nit, ParaFirmar.DerechoPeticion, ct))!.Id.Should().Be(delNit.GetProperty("id").GetGuid());
        (await resolutor.ResolverAsync(empresa, null, ParaFirmar.DerechoPeticion, ct))!.NumeroDocumento.Should().Be("79123456");
    }

    [Fact]
    public async Task SoloUnRepresentanteActivoDeEmpresa()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var cliente = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), Gestion));
        (await cliente.PostAsJsonAsync("/api/v1/comparendos/representantes", Rl("Empresa"), ct)).EnsureSuccessStatusCode();
        var otro = await cliente.PostAsJsonAsync("/api/v1/comparendos/representantes", Rl("Empresa", documento: "1010"), ct);
        otro.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await cliente.PostAsJsonAsync("/api/v1/comparendos/representantes", Rl("Empresa", documento: "1011", activo: false), ct)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Firma_SoloImagen()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var cliente = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), Gestion));
        var id = (await (await cliente.PostAsJsonAsync("/api/v1/comparendos/representantes", Rl("Empresa"), ct)).Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        using var pdf = new MultipartFormDataContent { { new ByteArrayContent("%PDF-1.4"u8.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") } }, "archivo", "firma.pdf" } };
        (await cliente.PostAsync($"/api/v1/comparendos/representantes/{id}/documentos/firma", pdf, ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using var png = new MultipartFormDataContent { { new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 1, 2, 3]) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, "archivo", "firma.png" } };
        var ok = await cliente.PostAsync($"/api/v1/comparendos/representantes/{id}/documentos/firma", png, ct);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var lista = await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/representantes", ct);
        lista[0].GetProperty("firmaArchivoId").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task AC2_BloquearCambioAnteUnOt()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        var cali = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000003");
        await using var scope = api.App.Services.CreateAsyncScope();
        var permisos = scope.ServiceProvider.GetRequiredService<PermisosOt>();
        (await permisos.DeAsync(empresa, cali, null, ct)).Should().Be((true, true));

        (await api.Cliente(new UsuarioDePrueba(empresa, Gestion)).PutAsJsonAsync("/api/v1/comparendos/organismos/permisos",
            new { organismoTransitoId = cali, permiteConsultaContraventor = true, permiteCambioContraventor = false }, ct)).EnsureSuccessStatusCode();

        (await permisos.DeAsync(empresa, cali, Guid.NewGuid(), ct)).Should().Be((true, false));
    }
}
