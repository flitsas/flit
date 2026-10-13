using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Infraestructura.Pdf;
using Flit.Comparendos.Api.Plataforma;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13483 — plantillas versionadas con editor y visor (Excel J2, J3).</summary>
public sealed class PlantillasTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    private HttpClient Super => api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

    [Fact]
    public async Task Semillas_UnDpPorTipologiaYLosCorreos()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var lista = await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/plantillas?conjunto=Flit", TestContext.Current.CancellationToken);
        lista.EnumerateArray().Count(p => p.GetProperty("tipo").GetString() == "DpDocumento").Should().Be(11);
        lista.EnumerateArray().Select(p => p.GetProperty("tipo").GetString()).Should().Contain(["CorreoAviso1", "CorreoDpOt", "FormatoCambioContraventor", "CartaCambioContraventor"]);
        lista.EnumerateArray().Should().OnlyContain(p => p.GetProperty("publicada").GetProperty("estado").GetString() == "Publicada");
    }

    [Fact]
    public async Task AC1_VariableDesconocida_NoSePublica_YAC2_PublicarRetiraLaAnterior()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var aviso = (await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/plantillas?tipo=CorreoAviso2", ct))[0];
        var id = aviso.GetProperty("id").GetGuid();

        var mala = await (await Super.PostAsJsonAsync($"/api/v1/comparendos/admin/plantillas/{id}/versiones", new { asunto = "x", cuerpoHtml = "<p>{{comparendo.placa_vehiculo}}</p>" }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        var rechazo = await Super.PostAsJsonAsync($"/api/v1/comparendos/admin/plantillas/versiones/{mala.GetProperty("id").GetGuid()}/publicar", new { nota = "prueba" }, ct);
        rechazo.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await rechazo.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("error").GetString().Should().Contain("{{comparendo.placa_vehiculo}}");

        await Super.PutAsJsonAsync($"/api/v1/comparendos/admin/plantillas/versiones/{mala.GetProperty("id").GetGuid()}", new { asunto = "Descuento {{comparendo.numero}}", cuerpoHtml = "<p>Hola {{conductor.nombres}}</p>" }, ct);
        (await Super.PostAsJsonAsync($"/api/v1/comparendos/admin/plantillas/versiones/{mala.GetProperty("id").GetGuid()}/publicar", new { nota = "Texto más corto" }, ct)).EnsureSuccessStatusCode();

        var detalle = await Super.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/plantillas/{id}", ct);
        var versiones = detalle.GetProperty("versiones").EnumerateArray().ToDictionary(v => v.GetProperty("numeroVersion").GetInt32(), v => v.GetProperty("estado").GetString());
        versiones[2].Should().Be("Publicada");
        versiones[1].Should().Be("Retirada");
        (await Super.PutAsJsonAsync($"/api/v1/comparendos/admin/plantillas/versiones/{mala.GetProperty("id").GetGuid()}", new { cuerpoHtml = "<p>x</p>" }, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task D9_CorreoDeRenting_SinRastroDeFlit()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var cliente = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [Permisos.PlantillasVer]));
        async Task<string> Html(string conjunto)
        {
            var r = await cliente.PostAsJsonAsync("/api/v1/comparendos/plantillas/vista-previa",
                new { tipo = "CorreoAviso1", asunto = "Aviso {{comparendo.numero}}", cuerpoHtml = "<p>Hola {{conductor.nombres}}</p>", formato = "html", conjunto }, ct);
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("html").GetString()!;
        }

        (await Html("Flit")).Should().Contain("FLIT");
        (await Html("Renting")).Should().NotContainEquivalentOf("flit", "D9: las comunicaciones de Renting no llevan nada de FLIT");
    }

    [Fact]
    public async Task CadaTesis_TieneSuTextoDeV1_ValidoYEnPdf()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var lector = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [Permisos.PlantillasVer]));
        var dps = (await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/plantillas?tipo=DpDocumento&conjunto=Flit", ct)).EnumerateArray().ToList();
        var cuerpos = new Dictionary<string, string>();
        foreach (var dp in dps)
        {
            var version = await Super.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/plantillas/versiones/{dp.GetProperty("publicada").GetProperty("id").GetGuid()}", ct);
            var cuerpo = version.GetProperty("cuerpoHtml").GetString()!;
            cuerpos[dp.GetProperty("tipologia").GetString() ?? "?"] = cuerpo;
            Flit.Comparendos.Domain.Plantillas.MotorPlantillas.Validar(cuerpo).Desconocidas.Should().BeEmpty();
            var r = await lector.PostAsJsonAsync("/api/v1/comparendos/plantillas/vista-previa", new { tipo = "DpDocumento", cuerpoHtml = cuerpo, formato = "pdf" }, ct);
            (await r.Content.ReadAsByteArrayAsync(ct)).Take(4).Should().Equal("%PDF"u8.ToArray());
        }

        cuerpos.Values.Distinct().Count().Should().BeGreaterThan(5, "cada tesis con texto en V1 lleva el suyo, no el genérico");
    }

    [Fact]
    public async Task VistaPrevia_DelDpEnPdf()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var dp = (await Super.GetFromJsonAsync<JsonElement>("/api/v1/comparendos/plantillas?tipo=DpDocumento", ct))[0];
        var version = await Super.GetFromJsonAsync<JsonElement>($"/api/v1/comparendos/plantillas/versiones/{dp.GetProperty("publicada").GetProperty("id").GetGuid()}", ct);

        var r = await api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [Permisos.PlantillasVer]))
            .PostAsJsonAsync("/api/v1/comparendos/plantillas/vista-previa", new { tipo = "DpDocumento", cuerpoHtml = version.GetProperty("cuerpoHtml").GetString(), formato = "pdf" }, ct);
        r.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await r.Content.ReadAsByteArrayAsync(ct)).Take(4).Should().Equal("%PDF"u8.ToArray());
    }

    [Fact]
    public void HtmlAPdf_TextoPlanoConListaTablaYFirma()
    {
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var html = $"<h2>Título</h2><p style=\"text-align:justify\">Hola <b>mundo</b> &amp; más</p><ol><li>uno</li><li>dos</li></ol><table><tr><th>A</th><td>1</td></tr></table><img src=\"data:image/png;base64,{png}\" width=\"120\">";
        HtmlAPdf.TextoPlano(html).Should().Contain("Hola mundo & más").And.Contain("- uno").And.Contain("A | 1").And.Contain("[imagen]");
        HtmlAPdf.Generar(html).Take(4).Should().Equal("%PDF"u8.ToArray());
    }
}
