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
            alcance = "Empresa", tipoDocumento = "CC", numeroDocumento = "52123456", nombres = "María Gómez", correo = "rl@gestora.test", cargo = "Representante legal",
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
            tipoDocumento = "CC", numeroDocumento = "1020304050", nombres = "Juan Pérez", correo = "juan@example.test", declaracionTratamientoDatos = true,
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
        var porNit = d.GetProperty("porNit").EnumerateArray().Single();
        porNit.GetProperty("total").GetInt32().Should().Be(4);
        porNit.GetProperty("deudaActiva").GetDecimal().Should().Be(1604100m, "la tabla por NIT suma la misma deuda que el indicador (B13-1)");

        (await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/reportes/nits", ct)).GetArrayLength().Should().Be(1);
        (await Super.PostAsync($"/api/v1/comparendos/admin/ingesta/trabajos/ReporteSemanal/ejecutar?empresaId={empresa}", null, ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Super.PostAsync("/api/v1/comparendos/admin/ingesta/trabajos/IngestaSimitNit/ejecutar", null, ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var radar = await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/reportes/radar", ct);
        radar.EnumerateArray().Single().GetProperty("id").GetGuid().Should().Be(ids["R4"]);
        var bandeja = await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/comparendos?rapido=Radar", ct);
        bandeja.GetProperty("items").EnumerateArray().Select(f => f.GetProperty("id").GetGuid()).Should().Equal([ids["R4"]], "el clic en el radar lleva a la bandeja con el mismo criterio (§7.8)");

        var p = await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/reportes/productividad?desde=2026-10-01&hasta=2026-10-09", ct);
        var fila = p.GetProperty("filas").EnumerateArray().Single();
        fila.GetProperty("usuarioId").GetGuid().Should().Be(usuario.Id);
        (fila.GetProperty("gestionados").GetInt32(), fila.GetProperty("dpEnviados").GetInt32(), fila.GetProperty("contraventoresAsignados").GetInt32(),
            fila.GetProperty("pagos").GetInt32(), fila.GetProperty("respuestasOt").GetInt32(), fila.GetProperty("avisosManuales").GetInt32()).Should().Be((4, 2, 1, 1, 2, 0));

        var detalle = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/reportes/productividad/detalle?usuarioId={usuario.Id}&metrica=dpEnviados&desde=2026-10-01&hasta=2026-10-09", ct);
        detalle.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).Should().BeEquivalentTo([ids["R2"], ids["R3"]]);
        // El detalle de «gestionados» dice lo que hizo el usuario con cada uno; R-4 solo tiene contraventor y no puede salir vacío.
        var gestionados = (await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/reportes/productividad/detalle?usuarioId={usuario.Id}&metrica=gestionados&desde=2026-10-01&hasta=2026-10-09", ct))
            .EnumerateArray().ToDictionary(x => x.GetProperty("id").GetGuid(), x => x.GetProperty("acciones").EnumerateArray().Select(a => a.GetString()).ToList());
        gestionados[ids["R4"]].Should().Equal("contraventoresAsignados");
        gestionados[ids["R1"]].Should().Equal("pagos");
        gestionados[ids["R2"]].Should().BeEquivalentTo("dpEnviados", "respuestasOt");
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
    public async Task S2_Dinero_EstadosDescuentosOrganismosPagosYFrentes()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, usuario, _) = await EscenarioAsync(ct);
        var nitOriginal = await api.EnDbAsync(db => db.IdentificacionesMonitoreadas.Where(n => n.TenantId == empresa).Select(n => n.Id).SingleAsync(ct));
        // Notificado hace casi dos años: ya caducó (1 año) y no ha prescrito (3 años); fuera de toda ventana de descuento.
        await IngresarAsync(api, empresa, Item("R-5", placa: "XYZ987", notificacion: new DateOnly(2025, 1, 15), valor: 500000m));
        var cliente = api.Cliente(usuario);

        var r = await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/reportes/dinero?desde=2026-10-01&hasta=2026-10-09", ct);
        var d = r.GetProperty("dinero");
        var estados = d.GetProperty("estados").EnumerateArray().ToDictionary(e => e.GetProperty("estado").GetString()!, e => (e.GetProperty("cantidad").GetInt32(), e.GetProperty("valor").GetDecimal()));
        estados.Keys.Should().Equal("SinNotificar", "EnTiempo", "Vencido", "Caducado", "Prescrito");
        estados["EnTiempo"].Should().Be((2, 1604100m), "R-1 está pagado y R-2 tiene DP favorable: no son deuda");
        estados["Caducado"].Should().Be((1, 500000m));
        (d.GetProperty("exigible").GetProperty("valor").GetDecimal(), d.GetProperty("noExigible").GetProperty("valor").GetDecimal()).Should().Be((1604100m, 500000m));
        var d50 = d.GetProperty("descuento").GetProperty("d50");
        (d50.GetProperty("cantidad").GetInt32(), d50.GetProperty("valor").GetDecimal(), d50.GetProperty("ahorroEstimado").GetDecimal()).Should().Be((2, 1604100m, 802050m));
        d.GetProperty("descuento").GetProperty("d25").GetProperty("cantidad").GetInt32().Should().Be(0);
        d.GetProperty("cobroCoactivo").GetProperty("cantidad").GetInt32().Should().Be(0);
        var organismos = d.GetProperty("organismos").EnumerateArray().ToList();
        organismos.Sum(o => o.GetProperty("valor").GetDecimal()).Should().Be(2104100m, "el top suma la misma deuda cuando hay menos de 10 organismos");
        organismos.Sum(o => o.GetProperty("exigible").GetDecimal()).Should().Be(1604100m);
        var pagado = d.GetProperty("pagado");
        (pagado.GetProperty("cantidad").GetInt32(), pagado.GetProperty("total").GetDecimal(), pagado.GetProperty("bc").GetDecimal(), pagado.GetProperty("cc").GetDecimal())
            .Should().Be((1, 604100m, 604100m, 0m));
        d.GetProperty("ahorroLogrado").GetDecimal().Should().Be(604100m, "el mismo ahorro que el indicador del dashboard");
        var mensual = d.GetProperty("mensual").EnumerateArray().ToList();
        mensual.Should().HaveCount(12);
        mensual[^1].GetProperty("periodo").GetString().Should().Be("2026-10");
        mensual[^1].GetProperty("pagado").GetDecimal().Should().Be(604100m);

        // Cada frente da la cifra que muestra la bandeja con el mismo filtro.
        var frentes = d.GetProperty("frentes");
        var contadores = await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/comparendos/contadores", ct);
        frentes.GetProperty("proximosAVencer").GetProperty("cantidad").GetInt32().Should().Be(contadores.GetProperty("proximosAVencer").GetInt32());
        frentes.GetProperty("sinContraventor").GetProperty("cantidad").GetInt32().Should().Be(contadores.GetProperty("sinContraventor").GetInt32());
        frentes.GetProperty("rechazadosOt").GetProperty("cantidad").GetInt32().Should().Be(contadores.GetProperty("rechazadosOt").GetInt32());
        foreach (var (frente, query) in new[] { ("sinGestion", "sinGestion=true"), ("vencidos", "estadoLegal=Vencido"), ("caducidadInmediata", "juridico=CaducidadInmediata") })
        {
            var bandeja = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos?{query}", ct);
            frentes.GetProperty(frente).GetProperty("cantidad").GetInt32().Should().Be(bandeja.GetProperty("total").GetInt32(), frente);
        }

        frentes.GetProperty("caducidadInmediata").GetProperty("valor").GetDecimal().Should().Be(500000m, "R-5 caducó y no tiene DP");

        // El NIT acota todo; otra empresa no ve nada; sin el módulo de reportes, 403.
        var porNit = (await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/reportes/dinero?nit={nitOriginal}", ct)).GetProperty("dinero");
        porNit.GetProperty("noExigible").GetProperty("valor").GetDecimal().Should().Be(0m);
        porNit.GetProperty("frentes").GetProperty("caducidadInmediata").GetProperty("cantidad").GetInt32().Should().Be(0);
        var otra = await EmpresaAsync(api, reportes: true);
        var ajena = (await api.Cliente(new UsuarioDePrueba(otra, Operador)).GetFromJsonAsync<JsonElement>("/api/v1/comparendos/reportes/dinero", ct)).GetProperty("dinero");
        (ajena.GetProperty("exigible").GetProperty("cantidad").GetInt32(), ajena.GetProperty("organismos").GetArrayLength()).Should().Be((0, 0));
        var sinModulo = await EmpresaAsync(api);
        (await api.Cliente(new UsuarioDePrueba(sinModulo, Operador)).GetAsync("/api/v1/comparendos/reportes/dinero", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.BandejaVer])).GetAsync("/api/v1/comparendos/reportes/dinero", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Super.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/reportes/dinero?empresa={empresa}", ct)).GetProperty("dinero").GetProperty("exigible").GetProperty("valor").GetDecimal()
            .Should().Be(1604100m);
    }

    [Fact]
    public async Task S2_Dinero_Bordes_ValorYOrganismoNulos_EmpresaAjena_YRangoInvalido()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, usuario, ids) = await EscenarioAsync(ct);
        // R-4 sin valor y sin organismo: cuenta como deuda, suma 0 y sale como «Sin organismo identificado», sin 500.
        await api.EnDbAsync(db => db.Comparendos.Where(c => c.Id == ids["R4"])
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.ValorTotal, (decimal?)null).SetProperty(c => c.OrganismoTransitoId, (Guid?)null), ct));
        var cliente = api.Cliente(usuario);

        var d = (await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/reportes/dinero", ct)).GetProperty("dinero");
        (d.GetProperty("exigible").GetProperty("cantidad").GetInt32(), d.GetProperty("exigible").GetProperty("valor").GetDecimal()).Should().Be((2, 604100m));
        var sinOrganismo = d.GetProperty("organismos").EnumerateArray().Single(o => o.GetProperty("organismoId").ValueKind == JsonValueKind.Null);
        (sinOrganismo.GetProperty("nombre").GetString(), sinOrganismo.GetProperty("cantidad").GetInt32(), sinOrganismo.GetProperty("valor").GetDecimal())
            .Should().Be(("Sin organismo identificado", 1, 0m));
        d.GetProperty("descuento").GetProperty("d50").GetProperty("ahorroEstimado").GetDecimal().Should().Be(302050m, "un comparendo sin valor no ahorra nada");

        var otra = await EmpresaAsync(api, reportes: true);
        (await cliente.GetAsync($"/api/v1/comparendos/reportes/dinero?empresa={otra}", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "una gestora no lee otra empresa");
        (await cliente.GetAsync("/api/v1/comparendos/reportes/dinero?desde=2026-10-09&hasta=2026-10-01", ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await cliente.GetAsync("/api/v1/comparendos/reportes/dinero?desde=2020-01-01&hasta=2026-10-09", ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "más de tres años");
        // El SuperAdmin sin empresa ve todas: al menos la deuda de esta.
        (await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/reportes/dinero", ct)).GetProperty("dinero").GetProperty("exigible").GetProperty("cantidad").GetInt32()
            .Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task S2_Dinero_PartesEstados_YCobroCoactivoComoLaBandeja()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (_, usuario, ids) = await EscenarioAsync(ct);
        // R-1 está pagado y R-4 es deuda: los dos en cobro coactivo. La bandeja cuenta los dos; la tarjeta también.
        await api.EnDbAsync(db => db.Comparendos.Where(c => c.Id == ids["R1"] || c.Id == ids["R4"])
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.EnCobroCoactivo, true), ct));
        var cliente = api.Cliente(usuario);

        var todo = (await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/reportes/dinero", ct)).GetProperty("dinero");
        var bandeja = await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/comparendos?cobroCoactivo=true", ct);
        (todo.GetProperty("cobroCoactivo").GetProperty("cantidad").GetInt32(), todo.GetProperty("cobroCoactivo").GetProperty("valor").GetDecimal())
            .Should().Be((bandeja.GetProperty("total").GetInt32(), 1604100m), "la cifra es la de la bandeja al hacer clic, pagados incluidos");
        bandeja.GetProperty("total").GetInt32().Should().Be(2);

        // ?partes=estados: solo el reparto, igual al de la respuesta completa.
        var solo = (await cliente.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/reportes/dinero?partes=estados", ct)).GetProperty("dinero");
        solo.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo(["hoy", "estados", "exigible", "noExigible"]);
        solo.GetProperty("exigible").GetRawText().Should().Be(todo.GetProperty("exigible").GetRawText());
        solo.GetProperty("noExigible").GetRawText().Should().Be(todo.GetProperty("noExigible").GetRawText());
        solo.GetProperty("estados").GetRawText().Should().Be(todo.GetProperty("estados").GetRawText());
        (await cliente.GetAsync("/api/v1/comparendos/reportes/dinero?partes=todo", ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
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
