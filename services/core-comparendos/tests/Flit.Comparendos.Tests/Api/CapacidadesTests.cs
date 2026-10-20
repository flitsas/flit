using System.Net;
using System.Net.Http.Json;
using Flit.Comparendos.Api.Plataforma;
using FluentAssertions;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13473 AC1 y Excel S1: la capacidad se valida en cada endpoint (403 CAPACIDAD_NO_HABILITADA), no solo en el menú.</summary>
public sealed class CapacidadesTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private static readonly string[] Todo =
    [
        Permisos.BandejaVer, Permisos.DpGenerar, Permisos.DpEnviar, Permisos.DpResponder, Permisos.CambioContraventorSolicitar,
        Permisos.CambioContraventorResponder, Permisos.AvisosEnviar,
    ];

    [Fact]
    public async Task SinLaCapacidad_CadaOperacionResponde403()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api, dp: false, cambio: false);
        var id = (await IngresarAsync(api, empresa, Item("CAP-1"))).Values.Single();
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Todo));
        var solicitud = Guid.NewGuid();

        var llamadas = new (string Nombre, Func<Task<HttpResponseMessage>> Llamada)[]
        {
            ("tipologias", () => cliente.GetAsync($"/api/v1/comparendos/comparendos/{id}/dp/tipologias", ct)),
            ("vista previa", () => cliente.PostAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/dp/vista-previa", new { tipologiaId = Guid.NewGuid() }, ct)),
            ("enviar DP", () => cliente.PostAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/dp", new { tipologiaId = Guid.NewGuid() }, ct)),
            ("respuesta del OT", () => cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/dp/respuesta", new MultipartFormDataContent(), ct)),
            ("solicitar cambio", () => cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/cambio-contraventor", null, ct)),
            ("aviso 4", () => cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/cambio-contraventor/aviso4", null, ct)),
            ("respuesta del cambio", () => cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/cambio-contraventor/{solicitud}/respuesta", new MultipartFormDataContent(), ct)),
        };
        foreach (var (nombre, llamada) in llamadas)
        {
            var r = await llamada();
            r.StatusCode.Should().Be(HttpStatusCode.Forbidden, nombre);
            (await r.Content.ReadAsStringAsync(ct)).Should().Contain("CAPACIDAD_NO_HABILITADA", nombre);
        }

        // La viabilidad sigue siendo informativa: dice por qué no se puede.
        (await cliente.GetAsync($"/api/v1/comparendos/comparendos/{id}/cambio-contraventor/viabilidad", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
