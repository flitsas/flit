using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.DerechosPeticion;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13488 (DP individual), #13489 (lote) y #13490 (respuesta del OT).</summary>
public sealed class DerechosPeticionTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private static readonly string[] Operador =
    [
        Permisos.BandejaVer, Permisos.DpGenerar, Permisos.DpEnviar, Permisos.DpEnviarMasivo, Permisos.DpResponder, Permisos.RepresentantesGestionar,
        Permisos.ConfiguracionEditar,
    ];

    private HttpClient Super => api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

    private async Task<Guid> TipologiaAsync(string codigo) =>
        (await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/admin/tipologias")).EnumerateArray().Single(t => t.GetProperty("codigo").GetString() == codigo).GetProperty("id").GetGuid();

    private async Task<(Guid Empresa, HttpClient Cliente)> EmpresaConDpAsync(bool conRepresentante = true, params string[] tipologias)
    {
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        (await Super.PatchAsJsonAsync("/api/v1/comparendos/admin/organismos/aaaaaaaa-0001-4000-8000-000000000001", new { correosRadicacion = new[] { "radicacion@movilidad.test" } })).EnsureSuccessStatusCode();
        var empresa = await EmpresaAsync(api, dp: true);
        foreach (var t in tipologias)
            (await Super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{empresa}/tipologias/{await TipologiaAsync(t)}", new { habilitada = true })).EnsureSuccessStatusCode();
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));
        if (conRepresentante)
        {
            var rl = await (await cliente.PostAsJsonAsync("/api/v1/comparendos/representantes", new
            {
                alcance = "Empresa", tipoDocumento = "CC", numeroDocumento = "52123456", nombres = "María Gómez", cargo = "Representante legal",
                direccionNotificacion = "Calle 100 # 9A-45", permisoEnvioDp = true, permisoCambioContraventor = true,
            })).Content.ReadFromJsonAsync<JsonElement>();
            const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
            using var firma = new MultipartFormDataContent { { new ByteArrayContent(Convert.FromBase64String(png)) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, "archivo", "firma.png" } };
            (await cliente.PostAsync($"/api/v1/comparendos/representantes/{rl.GetProperty("id").GetGuid()}/documentos/firma", firma)).EnsureSuccessStatusCode();
        }

        return (empresa, cliente);
    }

    [Fact]
    public async Task HU13488_VistaPrevia_Envio_YUnSoloDp()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, cliente) = await EmpresaConDpAsync(true, "DA");
        var id = (await IngresarAsync(api, empresa, Item("DP-1"))).Values.Single();
        var da = await TipologiaAsync("DA");

        var previa = await (await cliente.PostAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/dp/vista-previa", new { tipologiaId = da }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        previa.GetProperty("puedeEnviar").GetBoolean().Should().BeTrue();
        previa.GetProperty("radicado").GetString().Should().Be("DA-2026-XXXXXX");
        var pdf = await cliente.GetAsync($"/api/v1/comparendos/comparendos/{id}/dp/vista-previa.pdf?tipologiaId={da}", ct);
        (await pdf.Content.ReadAsByteArrayAsync(ct)).Take(4).Should().Equal("%PDF"u8.ToArray());

        var respuestaEnvio = await cliente.PostAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/dp", new { tipologiaId = da }, ct);
        var envio = await respuestaEnvio.Content.ReadFromJsonAsync<JsonElement>(ct);
        envio.GetProperty("radicado").GetString().Should().MatchRegex(@"^DA-2026-\d{6}$");
        envio.GetProperty("estado").GetString().Should().Be("Enviado");
        var c = await api.EnDbAsync(db => db.Comparendos.SingleAsync(x => x.Id == id, ct));
        c.EstadoGestion.Should().Be(EstadoGestion.DpEnviado);
        c.TieneDp.Should().BeTrue();
        var dp = await api.EnDbAsync(db => db.DerechosPeticion.SingleAsync(d => d.ComparendoId == id, ct));
        dp.FechaLimiteRespuestaOt.Should().Be(new DateOnly(2026, 11, 3), "15 días hábiles desde el 9-oct (12-oct y 2-nov festivos)");

        var segundo = await cliente.PostAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/dp", new { tipologiaId = da }, ct);
        (await segundo.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("YA_TIENE_DP");
    }

    [Fact]
    public async Task HU13488_Bloqueos_EstadoLegal_Pagado_YSinRepresentanteConAlerta()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, cliente) = await EmpresaConDpAsync(false, "DA", "DPR");
        await cliente.PutAsJsonAsync("/api/v1/comparendos/configuracion/correo", new { canalCorreo = "FlitSmtp", correosAlertaOperativa = new[] { "alertas@gestora.test" } }, ct);
        var ids = await IngresarAsync(api, empresa, Item("DP-2"), Item("DP-3"));

        var dpr = await cliente.PostAsJsonAsync($"/api/v1/comparendos/comparendos/{ids["DP2"]}/dp", new { tipologiaId = await TipologiaAsync("DPR") }, ct);
        (await dpr.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("ESTADO_LEGAL_NO_PERMITIDO");

        var sinRl = await cliente.PostAsJsonAsync($"/api/v1/comparendos/comparendos/{ids["DP2"]}/dp", new { tipologiaId = await TipologiaAsync("DA") }, ct);
        (await sinRl.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("SIN_REPRESENTANTE_LEGAL");
        (await api.EnDbAsync(db => db.Timeline.AnyAsync(t => t.ComparendoId == ids["DP2"] && t.Tipo == TipoEventoTimeline.DpBloqueadoSinRl, ct))).Should().BeTrue();
        await cliente.PostAsJsonAsync($"/api/v1/comparendos/comparendos/{ids["DP3"]}/dp", new { tipologiaId = await TipologiaAsync("DA") }, ct);
        (await api.EnDbAsync(db => db.Comunicaciones.CountAsync(x => x.TenantId == empresa && x.Tipo == TipoComunicacion.AlertaSinRepresentante, ct))).Should().Be(1, "máximo una alerta por empresa y día");

        await api.EnDbAsync(db => db.Comparendos.Where(x => x.Id == ids["DP3"]).ExecuteUpdateAsync(u => u.SetProperty(x => x.Pagado, true)));
        var previa = await (await cliente.PostAsJsonAsync($"/api/v1/comparendos/comparendos/{ids["DP3"]}/dp/vista-previa", new { tipologiaId = await TipologiaAsync("DA") }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        previa.GetProperty("bloqueos").EnumerateArray().Select(b => b.GetProperty("codigo").GetString()).Should().Contain("COMPARENDO_PAGADO");
    }

    [Fact]
    public async Task HU13490_RespuestaDelOt_QuedaBloqueada()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, cliente) = await EmpresaConDpAsync(true, "DA");
        var id = (await IngresarAsync(api, empresa, Item("DP-4"))).Values.Single();
        await cliente.PostAsJsonAsync($"/api/v1/comparendos/comparendos/{id}/dp", new { tipologiaId = await TipologiaAsync("DA") }, ct);

        MultipartFormDataContent Formulario() => new()
        {
            { new StringContent("OT-2026-555"), "radicadoOt" }, { new StringContent("2026-10-09"), "fecha" }, { new StringContent("Favorable"), "sentido" },
            { new ByteArrayContent("%PDF-1.4"u8.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") } }, "archivo", "respuesta.pdf" },
        };
        (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/dp/respuesta", Formulario(), ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await cliente.PostAsync($"/api/v1/comparendos/comparendos/{id}/dp/respuesta", Formulario(), ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var detalle = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/comparendos/{id}", ct);
        detalle.GetProperty("estadoProceso").GetString().Should().Be("RespuestaFavorable");
    }

    [Fact]
    public async Task HU13489_Lote_MismoOt_OmiteLosQueYaTienenDp()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (empresa, cliente) = await EmpresaConDpAsync(true, "DC", "DA");
        // Notificados hace más de un año: caducados (DC solo procede en caducidad).
        var ids = await IngresarAsync(api, empresa, Item("LT-1", notificacion: new DateOnly(2025, 9, 1)), Item("LT-2", notificacion: new DateOnly(2025, 9, 2)), Item("LT-3", notificacion: new DateOnly(2025, 9, 3)));
        await cliente.PostAsJsonAsync($"/api/v1/comparendos/comparendos/{ids["LT1"]}/dp", new { tipologiaId = await TipologiaAsync("DC") }, ct);

        var lote = await (await cliente.PostAsJsonAsync("/api/v1/comparendos/dp/lotes", new { tipologiaId = await TipologiaAsync("DC"), comparendoIds = ids.Values }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        var procesador = api.App.Services.GetRequiredService<ProcesadorLotesDp>();
        while (await procesador.ProcesarSiguienteAsync(ct))
        {
        }

        var detalle = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/dp/lotes/{lote.GetProperty("id").GetGuid()}", ct);
        detalle.GetProperty("estado").GetString().Should().Be("Completado");
        detalle.GetProperty("enviados").GetInt32().Should().Be(2);
        detalle.GetProperty("omitidos").GetInt32().Should().Be(1);
        detalle.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("numero").GetString() == "LT-1").GetProperty("motivo").GetString().Should().Contain("ya tiene");

        (await cliente.PostAsJsonAsync("/api/v1/comparendos/dp/lotes", new { tipologiaId = await TipologiaAsync("DA"), comparendoIds = ids.Values }, ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
