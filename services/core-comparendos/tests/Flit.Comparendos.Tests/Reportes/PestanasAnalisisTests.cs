using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Reportes;

/// <summary>
/// Reportes 2.0 (S3): pestañas Gestión, Derechos de petición y Plataforma. Gestión sin periodo es la operación del Inicio;
/// la tasa y el ahorro de DP son los del Resumen (R1); cada empresa ve solo lo suyo (R2); Plataforma es solo del SuperAdmin
/// y es el único sitio donde aparece la fuente (R3).
/// </summary>
public sealed class PestanasAnalisisTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private const string Base = "/api/v1/comparendos";

    private static readonly string[] Operador =
    [
        Permisos.BandejaVer, Permisos.DpGenerar, Permisos.DpEnviar, Permisos.DpResponder, Permisos.RepresentantesGestionar, Permisos.PagosRegistrar,
        Permisos.ReportesVer,
    ];

    private HttpClient Super => api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

    /// <summary>El escenario de <c>InicioOperacionTests</c>: tres de la misma placa (uno SOAT, uno RTM), uno que caduca en nov-2026 y uno pagado.</summary>
    private async Task<Guid> OperacionAsync(CancellationToken ct)
    {
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api, reportes: true);
        var ids = await IngresarAsync(api, empresa,
            Item("G-1", placa: "AAA111") with { CodigoInfraccion = "D02" },
            Item("G-2", placa: "AAA111") with { CodigoInfraccion = "C35" },
            Item("G-3", placa: "AAA111"),
            Item("G-4", placa: "BBB222", notificacion: new DateOnly(2025, 11, 14)),
            Item("G-5", placa: "CCC333", valor: 1000000m));
        await api.EnDbAsync(db => db.Comparendos.Where(c => c.Id == ids["G5"]).ExecuteUpdateAsync(u => u.SetProperty(c => c.Pagado, true), ct));
        return empresa;
    }

    [Fact]
    public async Task Gestion_SinPeriodo_YConElPeriodoCompleto_DaLasMismasCifrasQueElInicio()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await OperacionAsync(ct);
        var gestora = api.Cliente(new UsuarioDePrueba(empresa, [Permisos.BandejaVer, Permisos.ReportesVer]));

        var inicio = (await gestora.GetFromJsonAsync<JsonElement>($"{Base}/comparendos/operacion", ct)).GetProperty("operacion").GetRawText();
        var sinPeriodo = (await gestora.GetFromJsonAsync<JsonElement>($"{Base}/reportes/gestion", ct)).GetProperty("gestion");
        sinPeriodo.GetProperty("conPeriodo").GetBoolean().Should().BeFalse();
        sinPeriodo.GetProperty("operacion").GetRawText().Should().Be(inicio, "lo que salió del Inicio se ve igual en Gestión");

        // El periodo completo (los últimos 30 días cubren las imposiciones del 28-sep y los ingresos de hoy) da lo mismo.
        var completo = (await gestora.GetFromJsonAsync<JsonElement>($"{Base}/reportes/gestion?desde=2026-09-09&hasta=2026-10-09", ct)).GetProperty("gestion");
        completo.GetProperty("conPeriodo").GetBoolean().Should().BeTrue();
        completo.GetProperty("operacion").GetRawText().Should().Be(inicio);

        var top = completo.GetProperty("topOrganismos").EnumerateArray().ToList();
        top.Sum(o => o.GetProperty("cantidad").GetInt32()).Should().Be(4, "los organismos reparten lo pendiente (G-5 está pagado)");

        // Un periodo que no incluye la imposición deja lo pendiente en cero, y el embudo sigue siendo la cifra de la bandeja con ese periodo.
        var fuera = (await gestora.GetFromJsonAsync<JsonElement>($"{Base}/reportes/gestion?desde=2026-10-01&hasta=2026-10-09", ct)).GetProperty("gestion").GetProperty("operacion");
        fuera.GetProperty("resumen").GetProperty("pendientes").GetInt32().Should().Be(0);
        fuera.GetProperty("resumen").GetProperty("nuevos30").GetInt32().Should().Be(5, "los cinco entraron hoy, dentro del periodo");
        foreach (var (desde, op) in new[] { ("2026-10-01", fuera), ("2026-09-09", completo.GetProperty("operacion")) })
        {
            foreach (var paso in op.GetProperty("gestion").GetProperty("pasos").EnumerateArray())
            {
                var estado = paso.GetProperty("estadoProceso").GetString();
                var bandeja = await gestora.GetFromJsonAsync<JsonElement>($"{Base}/comparendos?estadoProceso={estado}&imposicionDesde={desde}&imposicionHasta=2026-10-09", ct);
                paso.GetProperty("cantidad").GetInt32().Should().Be(bandeja.GetProperty("total").GetInt32(), $"{estado} desde {desde}");
            }
        }

        // NIT: el propio filtra (los cuatro pendientes son de él); uno que no es de la empresa deja todo en cero, no ve nada ajeno.
        var nit = await api.EnDbAsync(db => db.IdentificacionesMonitoreadas.Where(i => i.TenantId == empresa).Select(i => i.Id).SingleAsync(ct));
        (await gestora.GetFromJsonAsync<JsonElement>($"{Base}/reportes/gestion?nit={nit}", ct)).GetProperty("gestion").GetProperty("operacion")
            .GetProperty("resumen").GetProperty("pendientes").GetInt32().Should().Be(4);
        (await gestora.GetFromJsonAsync<JsonElement>($"{Base}/reportes/gestion?nit={Guid.NewGuid()}", ct)).GetProperty("gestion").GetProperty("operacion")
            .GetProperty("resumen").GetProperty("pendientes").GetInt32().Should().Be(0);

        // El SuperAdmin con «todas las empresas» ve la suma de todas (al menos las de esta empresa).
        (await Super.GetFromJsonAsync<JsonElement>($"{Base}/reportes/gestion", ct)).GetProperty("gestion").GetProperty("operacion")
            .GetProperty("resumen").GetProperty("pendientes").GetInt32().Should().BeGreaterThanOrEqualTo(4);

        // RANGO_INVALIDO como el resto de Reportes (R8: nada de rango invertido).
        (await gestora.GetAsync($"{Base}/reportes/gestion?desde=2026-10-09&hasta=2026-10-01", ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private async Task<Guid> TipologiaAsync(string codigo) =>
        (await Super.GetFromJsonAsync<JsonElement>($"{Base}/admin/tipologias")).EnumerateArray().Single(t => t.GetProperty("codigo").GetString() == codigo).GetProperty("id").GetGuid();

    /// <summary>Empresa con DP: D-1 favorable, D-2 desfavorable (ambos respondidos el 9-oct) y D-3 enviado sin respuesta.</summary>
    private async Task<(Guid Empresa, HttpClient Cliente, Dictionary<string, Guid> Ids)> DpAsync(CancellationToken ct)
    {
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        (await Super.PatchAsJsonAsync($"{Base}/admin/organismos/aaaaaaaa-0001-4000-8000-000000000001", new { correosRadicacion = new[] { "radicacion@movilidad.test" } }, ct)).EnsureSuccessStatusCode();
        var empresa = await EmpresaAsync(api, dp: true, reportes: true);
        var da = await TipologiaAsync("DA");
        (await Super.PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/tipologias/{da}", new { habilitada = true }, ct)).EnsureSuccessStatusCode();
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));
        var rl = await (await cliente.PostAsJsonAsync($"{Base}/representantes", new
        {
            alcance = "Empresa", tipoDocumento = "CC", numeroDocumento = "52123456", nombres = "María Gómez", correo = "rl@gestora.test", cargo = "Representante legal",
            direccionNotificacion = "Calle 100 # 9A-45", permisoEnvioDp = true, permisoCambioContraventor = true,
        }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        using var firma = new MultipartFormDataContent { { new ByteArrayContent(Convert.FromBase64String(png)) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, "archivo", "firma.png" } };
        (await cliente.PostAsync($"{Base}/representantes/{rl.GetProperty("id").GetGuid()}/documentos/firma", firma, ct)).EnsureSuccessStatusCode();

        var ids = await IngresarAsync(api, empresa, Item("D-1"), Item("D-2"), Item("D-3", valor: 1000000m) with { CodigoInfraccion = "D02" });
        foreach (var numero in new[] { "D1", "D2", "D3" })
            (await cliente.PostAsJsonAsync($"{Base}/comparendos/{ids[numero]}/dp", new { tipologiaId = da }, ct)).EnsureSuccessStatusCode();
        foreach (var (numero, sentido) in new[] { ("D1", "Favorable"), ("D2", "Desfavorable") })
        {
            using var respuesta = new MultipartFormDataContent
            {
                { new StringContent($"OT-{numero}"), "radicadoOt" }, { new StringContent("2026-10-09"), "fecha" }, { new StringContent(sentido), "sentido" },
                { new ByteArrayContent("%PDF-1.4"u8.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") } }, "archivo", "respuesta.pdf" },
            };
            (await cliente.PostAsync($"{Base}/comparendos/{ids[numero]}/dp/respuesta", respuesta, ct)).EnsureSuccessStatusCode();
        }

        return (empresa, cliente, ids);
    }

    [Fact]
    public async Task Dp_LaTasaYElAhorroSonLosDelResumen_YLasFilasSumanSusCifras()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, cliente, ids) = await DpAsync(ct);
        // Dos meses después: el plazo de respuesta de D-3 ya pasó.
        api.Reloj.Hoy(new DateOnly(2026, 12, 15));
        const string periodo = "desde=2026-10-01&hasta=2026-12-15";

        var kpis = (await cliente.GetFromJsonAsync<JsonElement>($"{Base}/reportes/dashboard?{periodo}", ct)).GetProperty("kpis");
        var dp = (await cliente.GetFromJsonAsync<JsonElement>($"{Base}/reportes/dp?{periodo}", ct)).GetProperty("dp");
        dp.GetProperty("tasaExito").GetDecimal().Should().Be(kpis.GetProperty("tasaExitoDp").GetDecimal()).And.Be(0.5m);
        dp.GetProperty("ahorro").GetDecimal().Should().Be(kpis.GetProperty("ahorroAcumulado").GetDecimal()).And.Be(604100m);

        var organismos = dp.GetProperty("porOrganismo").EnumerateArray().ToList();
        organismos.Sum(o => o.GetProperty("favorables").GetInt32()).Should().Be(kpis.GetProperty("dpFavorables").GetInt32());
        organismos.Sum(o => o.GetProperty("desfavorables").GetInt32()).Should().Be(kpis.GetProperty("dpDesfavorables").GetInt32());
        (organismos.Sum(o => o.GetProperty("enviados").GetInt32()), organismos.Sum(o => o.GetProperty("sinRespuesta").GetInt32())).Should().Be((3, 1));
        // Tasa por fila (S0): misma fórmula y corte que el Resumen; ponderada por sus respondidos da la tasa del Resumen.
        foreach (var filas in new[] { organismos, dp.GetProperty("porInfraccion").EnumerateArray().ToList() })
        {
            var respondidos = filas.Sum(o => o.GetProperty("favorables").GetInt32() + o.GetProperty("desfavorables").GetInt32());
            var ponderada = filas.Where(o => o.GetProperty("tasaExito").ValueKind != JsonValueKind.Null)
                .Sum(o => o.GetProperty("tasaExito").GetDecimal() * (o.GetProperty("favorables").GetInt32() + o.GetProperty("desfavorables").GetInt32())) / respondidos;
            Math.Round(ponderada, 4).Should().Be(kpis.GetProperty("tasaExitoDp").GetDecimal());
        }

        // Una fila sin respuestas no tiene tasa («—»), no 0 %: D02 solo tiene a D-3, sin respuesta.
        dp.GetProperty("porInfraccion").EnumerateArray().Single(i => i.GetProperty("clave").GetString() == "D02")
            .GetProperty("tasaExito").ValueKind.Should().Be(JsonValueKind.Null);
        dp.GetProperty("porInfraccion").EnumerateArray().Select(i => (i.GetProperty("clave").GetString(), i.GetProperty("enviados").GetInt32()))
            .Should().Equal(("C29", 2), ("D02", 1));
        dp.GetProperty("cruce").EnumerateArray().Sum(c => c.GetProperty("enviados").GetInt32()).Should().Be(3);

        dp.GetProperty("positivos").EnumerateArray().Select(p => p.GetProperty("comparendoId").GetGuid()).Should().Equal([ids["D1"]]);
        dp.GetProperty("vencidos").EnumerateArray().Select(p => p.GetProperty("comparendoId").GetGuid()).Should().Equal([ids["D3"]], "enviado, sin respuesta y con el plazo vencido");
        var acumulado = dp.GetProperty("acumulado").EnumerateArray().ToList();
        acumulado.Should().HaveCount(76, "del 1-oct al 15-dic");
        (acumulado[^1].GetProperty("enviados").GetInt32(), acumulado[^1].GetProperty("respondidos").GetInt32()).Should().Be((3, 2));
        dp.GetProperty("consolidado").ValueKind.Should().Be(JsonValueKind.Null, "el consolidado empresa × organismo es solo del SuperAdmin con todas las empresas");

        // El SuperAdmin con «todas» ve el consolidado; con la empresa elegida, la misma tasa que la gestora.
        var todas = (await Super.GetFromJsonAsync<JsonElement>($"{Base}/reportes/dp?{periodo}", ct)).GetProperty("dp");
        todas.GetProperty("consolidado").EnumerateArray().Where(c => c.GetProperty("empresaId").GetGuid() == empresa)
            .Sum(c => c.GetProperty("enviados").GetInt32()).Should().Be(3);
        (await Super.GetFromJsonAsync<JsonElement>($"{Base}/reportes/dp?{periodo}&empresa={empresa}", ct)).GetProperty("dp")
            .GetProperty("tasaExito").GetDecimal().Should().Be(0.5m);
    }

    [Fact]
    public async Task CadaEmpresaVeSoloLoSuyo_YPlataformaEsSoloDelSuperAdmin()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, cliente, _) = await DpAsync(ct);
        var otra = await EmpresaAsync(api, reportes: true);
        var ajena = api.Cliente(new UsuarioDePrueba(otra, [Permisos.BandejaVer, Permisos.ReportesVer]));
        const string periodo = "desde=2026-10-01&hasta=2026-10-09";

        var gestion = (await ajena.GetFromJsonAsync<JsonElement>($"{Base}/reportes/gestion", ct)).GetProperty("gestion");
        gestion.GetProperty("operacion").GetProperty("resumen").GetProperty("pendientes").GetInt32().Should().Be(0);
        gestion.GetProperty("topOrganismos").GetArrayLength().Should().Be(0);
        var dp = (await ajena.GetFromJsonAsync<JsonElement>($"{Base}/reportes/dp?{periodo}", ct)).GetProperty("dp");
        (dp.GetProperty("enviados").GetInt32(), dp.GetProperty("porOrganismo").GetArrayLength(), dp.GetProperty("positivos").GetArrayLength()).Should().Be((0, 0, 0));
        foreach (var ruta in new[] { "gestion", "dp" })
        {
            (await ajena.GetAsync($"{Base}/reportes/{ruta}?empresa={empresa}", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{ruta}: otra empresa");
            (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.BandejaVer])).GetAsync($"{Base}/reportes/{ruta}", ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{ruta}: sin ReportesVer");
        }

        var sinModulo = await EmpresaAsync(api, reportes: false);
        (await api.Cliente(new UsuarioDePrueba(sinModulo, [Permisos.ReportesVer])).GetAsync($"{Base}/reportes/gestion", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "sin el módulo de reportes");

        // R3: nada de la fuente en las pestañas de la gestora.
        foreach (var ruta in new[] { $"reportes/gestion?{periodo}", $"reportes/dp?{periodo}" })
            (await cliente.GetStringAsync($"{Base}/{ruta}", ct)).Should().NotContainAny(["Simit", "Municipal", "prueba", "fuente"]);

        // Plataforma: 403 a quien no es SuperAdmin, aunque tenga reportes.
        (await cliente.GetAsync($"{Base}/admin/reportes/plataforma?{periodo}", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var p = await Super.GetFromJsonAsync<JsonElement>($"{Base}/admin/reportes/plataforma?{periodo}&empresa={empresa}", ct);
        var simit = p.GetProperty("fuentes").EnumerateArray().Single(f => f.GetProperty("fuente").GetString() == "SimitNit");
        (simit.GetProperty("consultas").GetInt32(), simit.GetProperty("obtenidos").GetInt32(), simit.GetProperty("nuevos").GetInt32()).Should().Be((1, 3, 3));
        p.GetProperty("erroresPorTipo").ValueKind.Should().Be(JsonValueKind.Null, "con una empresa elegida no hay errores técnicos sin empresa");
        (await Super.GetFromJsonAsync<JsonElement>($"{Base}/admin/reportes/plataforma?{periodo}", ct)).GetProperty("erroresPorTipo").ValueKind.Should().Be(JsonValueKind.Array);
        (await Super.GetAsync($"{Base}/admin/reportes/plataforma?desde=2025-01-01&hasta=2026-10-09", ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "más de un año");
    }
}
