using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// Epic #14606, ronda 2: ver el documento y el correo antes de aprobar o enviar (HU #14616 y #14619, backend). Ninguna de estas
/// vistas previas envía correos, crea DP ni reserva radicado. Los destinatarios de las pruebas son siempre falsos (@ejemplo.test).
/// </summary>
public sealed class VistaPreviaDpTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private const string Base = "/api/v1/comparendos";
    private static readonly string[] Operador = [Permisos.BandejaVer, Permisos.DpGenerar, Permisos.DpEnviar, Permisos.RepresentantesGestionar, Permisos.ConfiguracionEditar, Permisos.PlantillasVer];

    private HttpClient Super => api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

    private async Task<Guid> TipologiaAsync(string codigo) =>
        (await Super.GetFromJsonAsync<JsonElement>($"{Base}/admin/tipologias", TestContext.Current.CancellationToken))
        .EnumerateArray().Single(t => t.GetProperty("codigo").GetString() == codigo).GetProperty("id").GetGuid();

    private async Task<(int Dps, int Radicados, int Comunicaciones, int Archivos)> ConteosAsync() =>
        await api.EnDbAsync(async db => (await db.DerechosPeticion.CountAsync(), await db.RadicadosReservados.CountAsync(), await db.Comunicaciones.CountAsync(), await db.Archivos.CountAsync()));

    private async Task<Guid> PublicadaDpAsync(string codigo)
    {
        var ct = TestContext.Current.CancellationToken;
        var tipologia = await TipologiaAsync(codigo);
        var plantillas = await Super.GetFromJsonAsync<JsonElement>($"{Base}/plantillas?conjunto=Flit&tipo=DpDocumento", ct);
        return plantillas.EnumerateArray().Single(p => p.GetProperty("tipologiaId").GetGuid() == tipologia).GetProperty("publicada").GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task HU14616_La_version_dice_quien_aprobo_y_con_que_nota_y_otra_aprobacion_no_la_cambia()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var version = await PublicadaDpAsync("DCI");
        var antes = await Super.GetFromJsonAsync<JsonElement>($"{Base}/plantillas/versiones/{version}", ct);
        antes.GetProperty("revisadaPor").ValueKind.Should().Be(JsonValueKind.Null);
        antes.GetProperty("notaRevisionJuridica").ValueKind.Should().Be(JsonValueKind.Null);

        var primera = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true, Email: "abogada@ejemplo.test"));
        var aprobada = await (await primera.PostAsJsonAsync($"{Base}/admin/plantillas/versiones/{version}/revision-juridica", new { nota = "Texto aprobado por la abogada." }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        aprobada.GetProperty("revisadaPor").GetString().Should().Be("abogada@ejemplo.test");
        aprobada.GetProperty("notaRevisionJuridica").GetString().Should().Be("Texto aprobado por la abogada.");

        var otra = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true, Email: "otro@ejemplo.test"));
        (await otra.PostAsJsonAsync($"{Base}/admin/plantillas/versiones/{version}/revision-juridica", new { nota = "Otra nota." }, ct)).EnsureSuccessStatusCode();
        var despues = await Super.GetFromJsonAsync<JsonElement>($"{Base}/plantillas/versiones/{version}", ct);
        despues.GetProperty("revisadaPor").GetString().Should().Be("abogada@ejemplo.test");
        despues.GetProperty("notaRevisionJuridica").GetString().Should().Be("Texto aprobado por la abogada.");
    }

    [Fact]
    public async Task HU14616_Una_gestora_no_ve_quien_aprobo_ni_la_nota_y_un_nombre_largo_no_rompe_la_aprobacion()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var version = await PublicadaDpAsync("DA");
        var largo = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true, Email: new string('a', 236) + "@ejemplo.test"));

        var aprobada = await largo.PostAsJsonAsync($"{Base}/admin/plantillas/versiones/{version}/revision-juridica", new { nota = "Nota interna del abogado." }, ct);

        aprobada.EnsureSuccessStatusCode();
        var guardado = await api.EnDbAsync(db => db.PlantillaVersiones.AsNoTracking().Where(v => v.Id == version).Select(v => v.RevisadaJuridicamentePorNombre).SingleAsync(ct));
        guardado!.Length.Should().Be(200, "se recorta al largo de la columna");

        var super = await Super.GetFromJsonAsync<JsonElement>($"{Base}/plantillas/versiones/{version}", ct);
        super.GetProperty("revisadaPor").GetString().Should().NotBeNullOrEmpty();
        super.GetProperty("notaRevisionJuridica").GetString().Should().Be("Nota interna del abogado.");

        var gestora = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [Permisos.PlantillasVer]));
        var vista = await gestora.GetFromJsonAsync<JsonElement>($"{Base}/plantillas/versiones/{version}", ct);
        vista.GetProperty("revisadaPor").ValueKind.Should().Be(JsonValueKind.Null);
        vista.GetProperty("notaRevisionJuridica").ValueKind.Should().Be(JsonValueKind.Null);
        vista.GetProperty("revisadaJuridicamenteEn").ValueKind.Should().NotBe(JsonValueKind.Null, "que está aprobada sí se ve");
    }

    [Fact]
    public async Task HU14616_La_vista_previa_de_plantilla_sin_comparendo_usa_datos_de_ejemplo_y_no_crea_nada()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var version = await Super.GetFromJsonAsync<JsonElement>($"{Base}/plantillas/versiones/{await PublicadaDpAsync("DCI")}", ct);
        var cuerpo = version.GetProperty("cuerpoHtml").GetString();
        var antes = await ConteosAsync();

        var pdf = await Super.PostAsJsonAsync($"{Base}/plantillas/vista-previa", new { tipo = "DpDocumento", cuerpoHtml = cuerpo, formato = "pdf" }, ct);
        pdf.EnsureSuccessStatusCode();
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await pdf.Content.ReadAsByteArrayAsync(ct)).Take(4).Should().Equal("%PDF"u8.ToArray());

        var correo = await (await Super.PostAsJsonAsync($"{Base}/plantillas/vista-previa", new { tipo = "CorreoDpOt", asunto = "Derecho de petición {{dp.radicado}}", cuerpoHtml = "<p>Radicado {{dp.radicado}} de {{peticionario.nombre}}</p>" }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        correo.GetProperty("asunto").GetString().Should().NotBeNullOrWhiteSpace().And.NotContain("{{");
        var html = correo.GetProperty("html").GetString()!;
        html.Should().Contain("RENTING DE COLOMBIA S.A.S.").And.Contain("DATOS DE EJEMPLO");
        correo.GetProperty("ejemplo").GetBoolean().Should().BeTrue();

        (await ConteosAsync()).Should().Be(antes, "la vista previa no crea DP, radicado, correo ni archivo");
    }

    [Fact]
    public async Task HU14619_La_vista_previa_del_DP_trae_el_correo_armado_sin_enviar_nada_aun_con_bloqueos()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        (await Super.PatchAsJsonAsync($"{Base}/admin/organismos/aaaaaaaa-0001-4000-8000-000000000001", new { correosRadicacion = new[] { "radicacion@ejemplo.test" } }, ct)).EnsureSuccessStatusCode();
        var empresa = await EmpresaAsync(api, dp: true);
        var da = await TipologiaAsync("DA");
        (await Super.PutAsJsonAsync($"{Base}/admin/empresas/{empresa}/tipologias/{da}", new { habilitada = true }, ct)).EnsureSuccessStatusCode();
        var cliente = api.Cliente(new UsuarioDePrueba(empresa, Operador));
        var id = (await IngresarAsync(api, empresa, Item("VP-1"))).Values.Single();

        // Sin representante legal hay un bloqueo, pero la vista previa y el correo salen igual.
        var antes = await ConteosAsync();
        var previa = await (await cliente.PostAsJsonAsync($"{Base}/comparendos/{id}/dp/vista-previa", new { tipologiaId = da }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        previa.GetProperty("puedeEnviar").GetBoolean().Should().BeFalse();
        previa.GetProperty("bloqueos").EnumerateArray().Select(b => b.GetProperty("codigo").GetString()).Should().Contain("SIN_REPRESENTANTE_LEGAL");
        var correo = previa.GetProperty("correo");
        correo.GetProperty("asunto").GetString().Should().NotBeNullOrWhiteSpace().And.NotContain("{{");
        var html = correo.GetProperty("html").GetString()!;
        html.Should().NotBeNullOrWhiteSpace().And.Contain("DA-XXXXXXXXXXXX-XXX", "el radicado va enmascarado: no se reservó ninguno").And.NotContain("{{");
        (await ConteosAsync()).Should().Be(antes, "no se creó DP, radicado, comunicación ni archivo");
        (await api.EnDbAsync(db => db.Comparendos.AnyAsync(c => c.Id == id && c.TieneDp, ct))).Should().BeFalse();

        // Con representante y firma, lo mismo: sigue sin enviarse nada.
        var rl = await (await cliente.PostAsJsonAsync($"{Base}/representantes", new
        {
            alcance = "Empresa", tipoDocumento = "CC", numeroDocumento = "52123456", nombres = "María Gómez", correo = "rl@ejemplo.test", cargo = "Representante legal",
            direccionNotificacion = "Calle 100 # 9A-45", permisoEnvioDp = true, permisoCambioContraventor = true,
        }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        using var firma = new MultipartFormDataContent { { new ByteArrayContent(Convert.FromBase64String(png)) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, "archivo", "firma.png" } };
        (await cliente.PostAsync($"{Base}/representantes/{rl.GetProperty("id").GetGuid()}/documentos/firma", firma, ct)).EnsureSuccessStatusCode();
        var antes2 = await ConteosAsync();
        var lista = await (await cliente.PostAsJsonAsync($"{Base}/comparendos/{id}/dp/vista-previa", new { tipologiaId = da }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        lista.GetProperty("puedeEnviar").GetBoolean().Should().BeTrue();
        lista.GetProperty("correo").GetProperty("html").GetString().Should().Contain("DA-XXXXXXXXXXXX-XXX");
        (await ConteosAsync()).Should().Be(antes2);
    }
}
