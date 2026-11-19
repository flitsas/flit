using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Plataforma;
using FluentAssertions;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// HU #14614 (configuración del envío automático de derechos de petición) y la parte de #14612 que lo gobierna: sin una
/// versión de plantilla aprobada por un abogado, ninguna tipología se automatiza. Cada prueba usa una tipología distinta
/// porque el fixture (y sus plantillas publicadas) es compartido entre las pruebas de la clase.
/// </summary>
public sealed class EnvioAutomaticoDpConfiguracionTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private const string Base = "/api/v1/comparendos";

    private HttpClient Super => api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

    private async Task<Guid> TipologiaAsync(string codigo) =>
        (await Super.GetFromJsonAsync<JsonElement>($"{Base}/admin/tipologias", TestContext.Current.CancellationToken))
        .EnumerateArray().Single(t => t.GetProperty("codigo").GetString() == codigo).GetProperty("id").GetGuid();

    private async Task HabilitarAsync(Guid empresa, string codigo) =>
        (await Super.PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/tipologias/{await TipologiaAsync(codigo)}", new { habilitada = true }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

    private async Task<HttpResponseMessage> AutomatizarAsync(Guid empresa, string codigo, bool valor, HttpClient? cliente = null) =>
        await (cliente ?? Super).PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/tipologias/{await TipologiaAsync(codigo)}/automatizable", new { automatizable = valor }, TestContext.Current.CancellationToken);

    private async Task<bool> EsAutomatizableAsync(Guid empresa, string codigo) =>
        (await Super.GetFromJsonAsync<JsonElement>($"{Base}/admin/tipologias?empresa={empresa}", TestContext.Current.CancellationToken))
        .EnumerateArray().Single(t => t.GetProperty("codigo").GetString() == codigo).GetProperty("automatizable").GetBoolean();

    /// <summary>La plantilla del documento de una tipología en el conjunto Flit y su versión publicada.</summary>
    private async Task<(Guid Plantilla, Guid Version)> DocumentoAsync(string codigo)
    {
        var tipologia = await TipologiaAsync(codigo);
        var lista = await Super.GetFromJsonAsync<JsonElement>($"{Base}/plantillas?conjunto=Flit&tipo=DpDocumento", TestContext.Current.CancellationToken);
        var p = lista.EnumerateArray().Single(x => x.GetProperty("tipologiaId").GetGuid() == tipologia);
        return (p.GetProperty("id").GetGuid(), p.GetProperty("publicada").GetProperty("id").GetGuid());
    }

    private Task<HttpResponseMessage> AprobarAsync(Guid version, string nota = "Texto revisado por el abogado.") =>
        Super.PostAsJsonAsync($"{Base}/admin/plantillas/versiones/{version}/revision-juridica", new { nota }, TestContext.Current.CancellationToken);

    private async Task<JsonElement> ConfiguracionAsync(Guid empresa) =>
        await Super.GetFromJsonAsync<JsonElement>($"{Base}/admin/empresas/{empresa}/configuracion", TestContext.Current.CancellationToken);

    // ---------- AC1: apagado por defecto ----------

    [Fact]
    public async Task AC1_Una_empresa_nace_con_el_envio_automatico_apagado_y_ninguna_tipologia_automatizable()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaAsync(api, dp: true);
        await HabilitarAsync(empresa, "DCI");

        var config = await ConfiguracionAsync(empresa);

        config.GetProperty("dpAutomaticoActivo").GetBoolean().Should().BeFalse();
        config.GetProperty("dpAutomaticoTopeDiario").GetInt32().Should().Be(50);
        (await EsAutomatizableAsync(empresa, "DCI")).Should().BeFalse();
    }

    // ---------- Encender y fijar el tope ----------

    [Fact]
    public async Task El_SuperAdmin_enciende_el_envio_y_fija_el_tope()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaAsync(api, dp: true);

        var r = await Super.PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/envio-automatico-dp", new { activo = true, topeDiario = 80 }, TestContext.Current.CancellationToken);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var config = await ConfiguracionAsync(empresa);
        config.GetProperty("dpAutomaticoActivo").GetBoolean().Should().BeTrue();
        config.GetProperty("dpAutomaticoTopeDiario").GetInt32().Should().Be(80);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(501)]
    public async Task Un_tope_fuera_de_rango_se_rechaza_y_no_cambia_nada(int tope)
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaAsync(api, dp: true);

        var r = await Super.PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/envio-automatico-dp", new { activo = true, topeDiario = tope }, TestContext.Current.CancellationToken);

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ConfiguracionAsync(empresa)).GetProperty("dpAutomaticoActivo").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Sin_el_modulo_de_derechos_de_peticion_no_se_enciende()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaAsync(api, dp: false);

        var r = await Super.PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/envio-automatico-dp", new { activo = true, topeDiario = 50 }, TestContext.Current.CancellationToken);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("CAPACIDAD_DP_INACTIVA");
    }

    [Fact]
    public async Task Apagar_el_modulo_de_derechos_de_peticion_apaga_el_envio_automatico()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api, dp: true);
        (await Super.PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/envio-automatico-dp", new { activo = true, topeDiario = 50 }, ct)).EnsureSuccessStatusCode();

        (await Super.PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/capacidades", new
        {
            hasRightPetitionModule = false, allowsContravenerConsultation = false, allowsContravenerChange = false, hasOcrAccess = false, hasReportesModule = false,
            tipoIntegracionConductor = "Manual", requiereRepresentanteLegal = true,
        }, ct)).EnsureSuccessStatusCode();

        (await ConfiguracionAsync(empresa)).GetProperty("dpAutomaticoActivo").GetBoolean().Should().BeFalse();
    }

    // ---------- AC3: permiso ----------

    [Fact]
    public async Task AC3_Sin_ser_SuperAdmin_se_rechaza_con_403()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaAsync(api, dp: true);
        await HabilitarAsync(empresa, "DCI");
        var operador = api.Cliente(new UsuarioDePrueba(empresa, [Permisos.ConfiguracionEditar, Permisos.ConfiguracionVer]));

        (await operador.PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/envio-automatico-dp", new { activo = true, topeDiario = 50 }, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AutomatizarAsync(empresa, "DCI", true, operador)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await operador.PostAsJsonAsync($"{Base}/admin/plantillas/versiones/{Guid.NewGuid()}/revision-juridica", new { nota = "x" }, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ConfiguracionAsync(empresa)).GetProperty("dpAutomaticoActivo").GetBoolean().Should().BeFalse();
    }

    // ---------- AC4: solo con versión aprobada ----------

    [Fact]
    public async Task AC4_Sin_revision_juridica_la_tipologia_no_se_automatiza()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaAsync(api, dp: true);
        await HabilitarAsync(empresa, "DBN");

        var r = await AutomatizarAsync(empresa, "DBN", true);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("TIPOLOGIA_SIN_REVISION_JURIDICA");
        (await EsAutomatizableAsync(empresa, "DBN")).Should().BeFalse();
    }

    [Fact]
    public async Task AC4_Con_la_version_publicada_aprobada_la_tipologia_se_automatiza_y_se_puede_apagar()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaAsync(api, dp: true);
        await HabilitarAsync(empresa, "DCI");
        var (_, version) = await DocumentoAsync("DCI");
        (await AprobarAsync(version)).EnsureSuccessStatusCode();

        (await AutomatizarAsync(empresa, "DCI", true)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await EsAutomatizableAsync(empresa, "DCI")).Should().BeTrue();

        (await AutomatizarAsync(empresa, "DCI", false)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await EsAutomatizableAsync(empresa, "DCI")).Should().BeFalse();
    }

    [Fact]
    public async Task Publicar_una_version_nueva_apaga_la_marca_porque_el_texto_nuevo_no_esta_revisado()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaAsync(api, dp: true);
        await HabilitarAsync(empresa, "DPR");
        var (plantilla, version) = await DocumentoAsync("DPR");
        (await AprobarAsync(version)).EnsureSuccessStatusCode();
        (await AutomatizarAsync(empresa, "DPR", true)).EnsureSuccessStatusCode();
        (await EsAutomatizableAsync(empresa, "DPR")).Should().BeTrue();

        var nueva = await (await Super.PostAsJsonAsync($"{Base}/admin/plantillas/{plantilla}/versiones", new { asunto = "DP", cuerpoHtml = "<p>{{comparendo.numero}}</p>" }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        (await Super.PostAsJsonAsync($"{Base}/admin/plantillas/versiones/{nueva.GetProperty("id").GetGuid()}/publicar", new { nota = "Texto nuevo" }, ct)).EnsureSuccessStatusCode();

        (await EsAutomatizableAsync(empresa, "DPR")).Should().BeFalse("el texto nuevo nace sin revisión jurídica");
        (await AutomatizarAsync(empresa, "DPR", true)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Una_tipologia_no_habilitada_no_se_automatiza()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaAsync(api, dp: true);

        var r = await AutomatizarAsync(empresa, "DLL", true);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("TIPOLOGIA_NO_HABILITADA");
    }

    [Fact]
    public async Task Una_tipologia_sin_regla_automatica_solo_la_envia_una_persona()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaAsync(api, dp: true);
        await HabilitarAsync(empresa, "DA");
        var (_, version) = await DocumentoAsync("DA");
        (await AprobarAsync(version)).EnsureSuccessStatusCode();

        var r = await AutomatizarAsync(empresa, "DA", true);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("TIPOLOGIA_SIN_REGLA_AUTOMATICA");
    }

    // ---------- La revisión jurídica ----------

    [Fact]
    public async Task La_revision_exige_nota_y_una_version_publicada()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (plantilla, publicada) = await DocumentoAsync("DC");
        var borrador = await (await Super.PostAsJsonAsync($"{Base}/admin/plantillas/{plantilla}/versiones", new { asunto = "DC", cuerpoHtml = "<p>{{comparendo.numero}}</p>" }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);

        (await AprobarAsync(publicada, "   ")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await AprobarAsync(borrador.GetProperty("id").GetGuid())).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AprobarAsync(Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var ok = await AprobarAsync(publicada);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("revisadaJuridicamenteEn").ValueKind.Should().Be(JsonValueKind.String);
    }
}
