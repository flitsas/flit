using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Contraventor;
using Flit.Comparendos.Api.Modulos.Trabajos;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13478 — asignar el conductor y sus documentos.</summary>
public sealed class ContraventorTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private static readonly string[] Operador = [Permisos.BandejaVer, Permisos.ContraventorAsignar, Permisos.ContraventorConsultar, Permisos.ContraventorDocumentos];

    private static object Conductor(string doc, bool declaracion = true, string? motivo = null) => new
    {
        tipoDocumento = "CC", numeroDocumento = doc, nombres = $"Conductor {doc}", correo = $"c{doc}@ejemplo.com", telefono = "3000000000",
        direccion = "Calle 1", ciudad = "Bogotá", sucursal = (string?)null, tipoContrato = "Renting", motivo, declaracionTratamientoDatos = declaracion,
    };

    [Fact]
    public async Task AC1_Manual_ExigeDeclaracion_YAC2_ReemplazoExigeMotivoYDejaHistorial()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api);
        var id = (await IngresarAsync(api, empresa, Item("K-1")))["K1"];
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));

        (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", Conductor("111", declaracion: false), ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", Conductor("111"), ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", Conductor("222"), ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/contraventor", Conductor("222", motivo: "Contrato cedido"), ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detalle = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/{id}", ct);
        detalle.GetProperty("contraventor").GetProperty("numeroDocumento").GetString().Should().Be("222");
        detalle.GetProperty("gestionado").GetBoolean().Should().BeTrue();
        var historial = detalle.GetProperty("historialContraventor");
        historial.GetArrayLength().Should().Be(2);
        historial[0].GetProperty("motivo").GetString().Should().Be("Contrato cedido");
        historial[0].GetProperty("actorNombre").GetString().Should().NotBeNullOrWhiteSpace("la traza dice quién reemplazó al conductor");
        historial[0].GetProperty("datosAnteriores").GetString().Should().Contain("Conductor 111", "y quién era antes");
    }

    [Fact]
    public async Task ConsultaPorApi_EncuentraYAsigna_ONoEncuentra()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api, consulta: true, integracion: "ApiRenting");
        var ids = await IngresarAsync(api, empresa, Item("K-2", placa: "XYZ124"), Item("K-3", placa: "XYZ123"));
        ids.Keys.Should().HaveCount(2);
        (await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.Id == ids["K2"], ct))).EstadoConsultaContraventor.Should().Be(EstadoConsultaContraventor.Pendiente);
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));

        var par = await (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{ids["K2"]}/contraventor/consultar", null, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        par.GetProperty("resultado").GetString().Should().Be("Encontrado");
        var impar = await (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{ids["K3"]}/contraventor/consultar", null, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        impar.GetProperty("resultado").GetString().Should().Be("NoEncontrado");

        var k2 = await api.EnDbAsync(db => db.Comparendos.Include(c => c.Contraventor).SingleAsync(c => c.Id == ids["K2"], ct));
        k2.Contraventor!.Origen.Should().Be(OrigenContraventor.Api);
        (await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/comparendos?rapido=SinContraventor", ct)).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("numero").GetString()).Should().Equal("K-3");
    }

    [Fact]
    public async Task SinCapacidadDeConsulta_Responde403()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaAsync(api);
        var id = (await IngresarAsync(api, empresa, Item("K-4")))["K4"];
        var r = await api.Cliente(new UsuarioDePrueba(empresa, Operador)).PostAsync($"/api/v1/comparendos/comparendos/{id}/contraventor/consultar", null, TestContext.Current.CancellationToken);
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ReintentoDiario_ConsultaLosPendientes()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api, consulta: true, integracion: "ApiRenting");
        var ids = await IngresarAsync(api, empresa, Item("K-5", placa: "QWE128"), Item("K-6", placa: "QWE120"));

        await using (var scope = api.App.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetServices<IEjecutorTrabajo>().OfType<ReintentoContraventor>().Single().EjecutarAsync(new SolicitudTrabajo(TipoTrabajo.ReintentoContraventor, Disparo.Manual, empresa), ct);

        var k5 = await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.Id == ids["K5"], ct));
        var k6 = await api.EnDbAsync(db => db.Comparendos.SingleAsync(c => c.Id == ids["K6"], ct));
        k5.EstadoConsultaContraventor.Should().Be(EstadoConsultaContraventor.Encontrado);
        k6.EstadoConsultaContraventor.Should().Be(EstadoConsultaContraventor.Error);
        k6.IntentosConsultaContraventor.Should().Be(1);
    }

    [Fact]
    public async Task Documentos_LaNuevaCedulaReemplazaALaVigente()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api);
        var id = (await IngresarAsync(api, empresa, Item("K-7")))["K7"];
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));
        for (var i = 0; i < 2; i++)
        {
            using var form = new MultipartFormDataContent { { new ByteArrayContent("%PDF-1.4"u8.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") } }, "archivo", $"cedula{i}.pdf" } };
            (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/documentos?tipo=Cedula", form, ct)).EnsureSuccessStatusCode();
        }

        var docs = (await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/{id}", ct)).GetProperty("documentos");
        docs.GetArrayLength().Should().Be(2);
        docs.EnumerateArray().Count(d => d.GetProperty("vigente").GetBoolean()).Should().Be(1);
    }
}
