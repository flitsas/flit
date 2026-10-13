using System.Net;
using System.Net.Http.Json;
using System.Text;
using Flit.Comparendos.Api.Modulos.Ingesta;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>QA y PDN antes de conectar las integraciones: sin modo simulado.</summary>
public sealed class ApiSinSimulacion : ApiDePrueba
{
    protected override IDictionary<string, string?> Configuracion() => new Dictionary<string, string?> { ["Comparendos:Simulacion:Habilitada"] = "false" };
}

/// <summary>Fuera de DEV lo que no está conectado no inventa datos: no hay ciclos de ingesta, ni OCR, ni conductores de prueba.</summary>
public sealed class SimulacionTests(ApiSinSimulacion api) : IClassFixture<ApiSinSimulacion>
{
    [Fact]
    public async Task SinSimulacion_NoHayCiclos_NiOcr()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        var empresa = Guid.NewGuid();
        (await super.PostAsJsonAsync("/api/v1/comparendos/admin/empresas", new { id = empresa, nombre = "Gestora" }, ct)).EnsureSuccessStatusCode();
        (await super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{empresa}/capacidades", new
        {
            hasRightPetitionModule = false, allowsContravenerConsultation = false, allowsContravenerChange = false, hasOcrAccess = true, hasReportesModule = false,
            tipoIntegracionConductor = "Manual", conjuntoPlantillas = "Flit", requiereRepresentanteLegal = true, empresaNombre = "Gestora",
        }, ct)).EnsureSuccessStatusCode();

        var manual = await super.PostAsJsonAsync("/api/v1/comparendos/admin/ingesta/ejecutar", new { canal = "SimitNit" }, ct);
        manual.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await manual.Content.ReadAsStringAsync(ct)).Should().Contain("CONSULTA_NO_HABILITADA", "sin Identidad ni simulación ninguna empresa cuenta como habilitada");

        await using (var scope = api.App.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<CicloDeIngesta>().EjecutarAsync(CanalIngesta.SimitNit, Disparo.Programado, null, null, ct)).Should().Be(Guid.Empty);
        (await api.EnDbAsync(db => db.CiclosIngesta.CountAsync(ct))).Should().Be(0);

        var form = new MultipartFormDataContent { { new StringContent("Lote"), "nombre" }, { new ByteArrayContent(Encoding.UTF8.GetBytes("x")), "archivos", "a.jpg" } };
        var ocr = await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.OcrGestionar])).PostAsync("/api/v1/comparendos/ocr/paquetes", form, ct);
        ocr.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ocr.Content.ReadAsStringAsync(ct)).Should().Contain("OCR_NO_CONFIGURADO");
    }
}
