using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Reportes;
using Flit.Comparendos.Api.Modulos.Trabajos;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13498 — dashboard ejecutivo, productividad y reportes automáticos (§6.12, §6.16).</summary>
public sealed class ReportesTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private static readonly string[] Operador =
    [
        Permisos.BandejaVer, Permisos.DpGenerar, Permisos.DpEnviar, Permisos.DpResponder, Permisos.RepresentantesGestionar, Permisos.PagosRegistrar,
        Permisos.ContraventorAsignar, Permisos.ReportesVer, Permisos.ProductividadVer,
    ];

    private HttpClient Super => api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

    private async Task<Guid> TipologiaAsync(string codigo) =>
        (await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/tipologias")).EnumerateArray().Single(t => t.GetProperty("codigo").GetString() == codigo).GetProperty("id").GetGuid();

    /// <summary>Empresa con reportes y DP; cuatro comparendos: uno pagado, uno con DP favorable, uno con DP desfavorable y uno con contraventor.</summary>
    private async Task<(Guid Empresa, UsuarioDePrueba Usuario, Dictionary<string, Guid> Ids)> EscenarioAsync(CancellationToken ct)
    {
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        (await Super.PatchAsJsonAsync("/api/v1/comparendos/admin/organismos/aaaaaaaa-0001-4000-8000-000000000001", new { correosRadicacion = new[] { "radicacion@movilidad.test" } }, ct)).EnsureSuccessStatusCode();
        var empresa = await EmpresaAsync(api, dp: true, reportes: true);
        var da = await TipologiaAsync("DA");
        (await Super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{empresa}/tipologias/{da}", new { habilitada = true }, ct)).EnsureSuccessStatusCode();
        var usuario = new UsuarioDePrueba(empresa, Operador);
        var cliente = api.Cliente(usuario);
        var rl = await (await cliente.PostAsJsonAsync("/api/v1/comparendos/representantes", new
        {
            alcance = "Empresa", tipoDocumento = "CC", numeroDocumento = "52123456", nombres = "María Gómez", cargo = "Representante legal",
            direccionNotificacion = "Calle 100 # 9A-45", permisoEnvioDp = true, permisoCambioContraventor = true,
        }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        using var firma = new MultipartFormDataContent { { new ByteArrayContent(Convert.FromBase64String(png)) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, "archivo", "firma.png" } };
        (await cliente.PostAsync($"/api/v1/comparendos/representantes/{rl.GetProperty("id").GetGuid()}/documentos/firma", firma, ct)).EnsureSuccessStatusCode();

        var ids = await IngresarAsync(api, empresa, Item("R-1"), Item("R-2"), Item("R-3"), Item("R-4", valor: 1000000m));
        using var pago = new MultipartFormDataContent { { new StringContent("BC"), "tipo" }, { new StringContent("604100"), "valor" }, { new StringContent("2026-10-08"), "fecha" } };
        (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{ids["R1"]}/pago", pago, ct)).EnsureSuccessStatusCode();
        foreach (var (numero, sentido) in new[] { ("R2", "Favorable"), ("R3", "Desfavorable") })
        {
            (await cliente.PostAsJsonAsync($"/api/v1/comparendos/comparendos/{ids[numero]}/dp", new { tipologiaId = da }, ct)).EnsureSuccessStatusCode();
            using var respuesta = new MultipartFormDataContent
            {
                { new StringContent($"OT-{numero}"), "radicadoOt" }, { new StringContent("2026-10-09"), "fecha" }, { new StringContent(sentido), "sentido" },
                { new ByteArrayContent("%PDF-1.4"u8.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") } }, "archivo", "respuesta.pdf" },
            };
            (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{ids[numero]}/dp/respuesta", respuesta, ct)).EnsureSuccessStatusCode();
        }

        (await cliente.PutAsJsonAsync($"/api/v1/comparendos/comparendos/{ids["R4"]}/contraventor", new
        {
            tipoDocumento = "CC", numeroDocumento = "1020304050", nombres = "Juan Pérez", correo = "juan@ejemplo.com", declaracionTratamientoDatos = true,
        }, ct)).EnsureSuccessStatusCode();
        return (empresa, usuario, ids);
    }

    [Fact]
    public async Task AC1_Dashboard_Kpis_Series_YProductividad()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, usuario, ids) = await EscenarioAsync(ct);
        var cliente = api.Cliente(usuario);

        var d = await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/reportes/dashboard?desde=2026-10-01&hasta=2026-10-09", ct);
        var k = d.GetProperty("kpis");
        k.GetProperty("deudaActivaTotal").GetDecimal().Should().Be(1604100m, "R-1 está pagado y R-2 tiene DP favorable");
        k.GetProperty("comparendosConDeuda").GetInt32().Should().Be(2);
        (k.GetProperty("dpFavorables").GetInt32(), k.GetProperty("dpDesfavorables").GetInt32(), k.GetProperty("tasaExitoDp").GetDecimal()).Should().Be((1, 1, 0.5m));
        k.GetProperty("ahorroAcumulado").GetDecimal().Should().Be(604100m);
        (k.GetProperty("radarCantidad").GetInt32(), k.GetProperty("radarValor").GetDecimal()).Should().Be((1, 1000000m), "solo R-4 sigue sin DP ni pago y vence el 19-oct");
        var octubre = d.GetProperty("mensual").EnumerateArray().Last();
        octubre.GetProperty("periodo").GetString().Should().Be("2026-10");
        (octubre.GetProperty("ingresados").GetInt32(), octubre.GetProperty("dpEnviados").GetInt32(), octubre.GetProperty("dpFavorables").GetInt32(), octubre.GetProperty("pagosBc").GetInt32())
            .Should().Be((4, 2, 1, 1));
        d.GetProperty("anual").GetProperty("actual").GetArrayLength().Should().Be(12);
        d.GetProperty("porNit").EnumerateArray().Single().GetProperty("total").GetInt32().Should().Be(4);

        (await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/reportes/nits", ct)).GetArrayLength().Should().Be(1);
        (await Super.PostAsync($"/api/v1/comparendos/admin/ingesta/trabajos/ReporteSemanal/ejecutar?empresaId={empresa}", null, ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Super.PostAsync("/api/v1/comparendos/admin/ingesta/trabajos/IngestaSimitNit/ejecutar", null, ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var radar = await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/reportes/radar", ct);
        radar.EnumerateArray().Single().GetProperty("id").GetGuid().Should().Be(ids["R4"]);

        var p = await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/reportes/productividad?desde=2026-10-01&hasta=2026-10-09", ct);
        var fila = p.GetProperty("filas").EnumerateArray().Single();
        fila.GetProperty("usuarioId").GetGuid().Should().Be(usuario.Id);
        (fila.GetProperty("gestionados").GetInt32(), fila.GetProperty("dpEnviados").GetInt32(), fila.GetProperty("contraventoresAsignados").GetInt32(),
            fila.GetProperty("pagos").GetInt32(), fila.GetProperty("respuestasOt").GetInt32(), fila.GetProperty("avisosManuales").GetInt32()).Should().Be((4, 2, 1, 1, 2, 0));

        var detalle = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/reportes/productividad/detalle?usuarioId={usuario.Id}&metrica=dpEnviados&desde=2026-10-01&hasta=2026-10-09", ct);
        detalle.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).Should().BeEquivalentTo([ids["R2"], ids["R3"]]);
        (await cliente.GetAsync($"/api/v1/comparendos/reportes/productividad/detalle?usuarioId={usuario.Id}&metrica=otra", ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var excel = await cliente.GetAsync("/api/v1/comparendos/reportes/productividad/excel", ct);
        excel.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        // El SuperAdmin ve todas las empresas o una; una empresa sin el módulo no ve el dashboard.
        var todas = await Super.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/reportes/dashboard?empresa={empresa}", ct);
        todas.GetProperty("kpis").GetProperty("deudaActivaTotal").GetDecimal().Should().Be(1604100m);
        var sinModulo = await EmpresaAsync(api);
        (await api.Cliente(new UsuarioDePrueba(sinModulo, Operador)).GetAsync("/api/v1/comparendos/reportes/dashboard", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC2_ReporteSemanal_ADestinatarios_ConCopiaOcultaInterna_UnaVezPorDia()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, _, _) = await EscenarioAsync(ct);
        await api.EnDbAsync(db => db.ConfiguracionesEmpresa.Where(e => e.TenantId == empresa)
            .ExecuteUpdateAsync(u => u.SetProperty(e => e.DestinatariosReportes, ["gerencia@cliente.test"]), ct));
        var config = await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/alertas", ct);
        (await Super.PutAsJsonAsync("/api/v1/comparendos/admin/alertas", new
        {
            alertasHabilitadas = config.GetProperty("alertasHabilitadas").GetBoolean(), umbralFallosCiclo = config.GetProperty("umbralFallosCiclo").GetInt32(),
            plantillaAsunto = config.GetProperty("plantillaAsunto").GetString(), remitenteNombre = config.GetProperty("remitenteNombre").GetString(),
            correosInternosReportes = new[] { "Cuentas@FLIT.test" },
        }, ct)).EnsureSuccessStatusCode();
        (await Super.PutAsJsonAsync("/api/v1/comparendos/admin/alertas", new { alertasHabilitadas = true, umbralFallosCiclo = 0, plantillaAsunto = "x", remitenteNombre = "y", correosInternosReportes = new[] { "no-es-correo" } }, ct))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        async Task CorrerAsync()
        {
            await using var scope = api.App.Services.CreateAsyncScope();
            var ejecutor = scope.ServiceProvider.GetServices<IEjecutorTrabajo>().OfType<ReportesAutomaticos>().Single();
            await ejecutor.EjecutarAsync(new SolicitudTrabajo(TipoTrabajo.ReporteSemanal, Disparo.Programado), CancellationToken.None);
        }

        await CorrerAsync();
        await CorrerAsync();
        var correos = await api.EnDbAsync(db => db.Comunicaciones.Where(c => c.TenantId == empresa && c.Tipo == TipoComunicacion.ReporteSemanal).ToListAsync(ct));
        var correo = correos.Should().ContainSingle("un cierre no sale dos veces el mismo día").Subject;
        correo.Destinatarios.Should().Contain("gerencia@cliente.test").And.Contain("cuentas@flit.test");
        correo.Asunto.Should().Contain("Cierre semanal").And.Contain("5 de octubre de 2026");
    }
}
