using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Plataforma;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13458 — NITs y placas monitoreadas.</summary>
public sealed class MonitoreoTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    private static readonly string[] Gestion = [Permisos.MonitoreoVer, Permisos.MonitoreoGestionar];

    private static UsuarioDePrueba Admin(Guid empresa) => new(empresa, Gestion);

    [Fact]
    public async Task AC1_RegistrarNit_GuardaBaseYDigitoYAudita()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();

        var r = await api.Cliente(Admin(empresa)).PostAsJsonAsync("/api/v1/comparendos/monitoreo/nits", new { numero = "890.903.938-8", nombreTitular = "Bancolombia" }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var nit = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
        nit.GetProperty("numeroBase").GetString().Should().Be("890903938");
        nit.GetProperty("digitoVerificacion").GetInt16().Should().Be(8);
        (await api.EnDbAsync(db => db.AuditoriasConfiguracion.CountAsync(a => a.TenantId == empresa && a.Entidad == "identificacion_monitoreada", ct))).Should().Be(1);
        var historial = await api.Cliente(Admin(empresa)).GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/monitoreo/nits/{nit.GetProperty("id").GetGuid()}/historial", ct);
        historial.GetProperty("items").EnumerateArray().Single().GetProperty("accion").GetString().Should().Be("CREAR", "§7.12: el historial del NIT dice quién lo registró");
    }

    [Fact]
    public async Task AC2_DigitoErrado_SeRechaza()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;

        var r = await api.Cliente(Admin(Guid.NewGuid())).PostAsJsonAsync("/api/v1/comparendos/monitoreo/nits", new { numero = "890903938-5" }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await r.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("DV_INVALIDO");
    }

    [Fact]
    public async Task AC3_PlacaRepetida_NoSeDuplica()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var cliente = api.Cliente(Admin(Guid.NewGuid()));

        await cliente.PostAsJsonAsync("/api/v1/comparendos/monitoreo/placas", new { placas = new[] { "ABC123" } }, ct);
        var r = await cliente.PostAsJsonAsync("/api/v1/comparendos/monitoreo/placas", new { placas = new[] { "abc-123", "XYZ987", "??" } }, ct);

        var resultado = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
        resultado.GetProperty("yaExistian").EnumerateArray().Select(e => e.GetString()).Should().Equal("ABC123");
        resultado.GetProperty("agregadas").EnumerateArray().Select(e => e.GetProperty("placa").GetString()).Should().Equal("XYZ987");
        resultado.GetProperty("invalidas").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task PlacaAgregadaDesdeElOcr_QuedaConOrigenOcr()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var cliente = api.Cliente(Admin(Guid.NewGuid()));

        var r = await (await cliente.PostAsJsonAsync("/api/v1/comparendos/monitoreo/placas", new { placas = new[] { "OCR123" }, origen = "OCR" }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        r.GetProperty("agregadas")[0].GetProperty("origenRegistro").GetString().Should().Be("OCR", "§3.4: la placa que se agrega desde el flujo OCR queda con origen OCR");
        (await cliente.PostAsJsonAsync("/api/v1/comparendos/monitoreo/placas", new { placas = new[] { "OCR124" }, origen = "OTRO" }, ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task OtraEmpresa_NoVeNiCambiaLoAjeno()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var a = Guid.NewGuid();
        var creado = await (await api.Cliente(Admin(a)).PostAsJsonAsync("/api/v1/comparendos/monitoreo/nits", new { numero = "899999068" }, ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        var otra = api.Cliente(Admin(Guid.NewGuid()));
        (await otra.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/monitoreo/nits", ct)).GetArrayLength().Should().Be(0);
        var cambio = await otra.PatchAsJsonAsync($"/api/v1/comparendos/monitoreo/nits/{creado.GetProperty("id").GetGuid()}", new { activo = false }, ct);
        cambio.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await otra.GetAsync($"/api/v1/comparendos/monitoreo/nits?empresa={a}", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SuperAdmin_LeeConEmpresaYNoOpera()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        await api.Cliente(Admin(empresa)).PostAsJsonAsync("/api/v1/comparendos/monitoreo/placas", new { placas = new[] { "KLM456" } }, ct);
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

        (await super.GetAsync("/api/v1/comparendos/monitoreo/placas", ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await super.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/monitoreo/placas?empresa={empresa}", ct)).GetArrayLength().Should().Be(1);
        var crear = await super.PostAsJsonAsync("/api/v1/comparendos/monitoreo/placas", new { placas = new[] { "QWE111" } }, ct);
        crear.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SinPermisoDeGestion_NoPuedeAgregar()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var r = await api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [Permisos.MonitoreoVer]))
            .PostAsJsonAsync("/api/v1/comparendos/monitoreo/placas", new { placas = new[] { "ABC123" } }, TestContext.Current.CancellationToken);
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static MultipartFormDataContent Excel(params string[][] filas)
    {
        var hoja = new Flit.Comparendos.Api.Infraestructura.Excel.TabularWorkbookWriter.Sheet("Placas",
            [.. filas[0].Select(e => new Flit.Comparendos.Api.Infraestructura.Excel.TabularWorkbookWriter.Column(e, 12))],
            [.. filas.Skip(1).Select(f => (IReadOnlyList<Flit.Comparendos.Api.Infraestructura.Excel.TabularWorkbookWriter.Cell>)[.. f.Select(Flit.Comparendos.Api.Infraestructura.Excel.TabularWorkbookWriter.Cell.Of)])]);
        var bytes = Flit.Comparendos.Api.Infraestructura.Excel.TabularWorkbookWriter.Write([hoja]);
        return new MultipartFormDataContent { { new ByteArrayContent(bytes), "archivo", "placas.xlsx" } };
    }

    /// <summary>Hallazgo B3-2 (S-64): placas por archivo Excel, con el mismo resultado que el pegado.</summary>
    [Fact]
    public async Task PlacasPorExcel_AgregaEInformaElResto()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var cliente = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [Permisos.MonitoreoGestionar, Permisos.MonitoreoVer]));

        var r = await cliente.PostAsync("/api/v1/comparendos/monitoreo/placas/archivo", Excel(["placa"], ["xls-101"], ["XLS101"], ["XLS102"], ["AB1"]), ct);
        r.EnsureSuccessStatusCode();
        var cuerpo = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
        cuerpo.GetProperty("agregadas").EnumerateArray().Select(p => p.GetProperty("placa").GetString()).Should().BeEquivalentTo(["XLS101", "XLS102"]);
        cuerpo.GetProperty("repetidas").EnumerateArray().Select(p => p.GetString()).Should().Equal("XLS101");
        cuerpo.GetProperty("invalidas").EnumerateArray().Select(p => p.GetString()).Should().Equal("AB1");

        var sinColumna = await cliente.PostAsync("/api/v1/comparendos/monitoreo/placas/archivo", Excel(["vehiculo"], ["XLS103"]), ct);
        (await sinColumna.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("FALTA_COLUMNA_PLACA");
        (await cliente.GetAsync("/api/v1/comparendos/monitoreo/placas/plantilla", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
