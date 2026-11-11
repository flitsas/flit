using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Backoffice;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Catalogos;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// ADR-0078 — <c>GET/PUT /backoffice/fuentes-obligatorias</c>. Catálogo global de FLIT: la política
/// <c>diagnostico.backoffice</c> sí aplica y <c>?empresa=</c> no. El PUT rechaza <c>RUNT</c>, un <c>SIMIT_*</c> en el
/// unilateral, quitar el piso y un alcance ambiguo. Guardar <c>SIMIT_VEHICULO</c> en el ordinario se ve en el
/// <c>GET /tipos-tramite</c> siguiente aunque la copia de Trámites siga vigente. <c>fuentes: []</c> deja la cabecera.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --filter FullyQualifiedName~FuentesObligatoriasBackofficeTests
/// </code>
/// </example>
public sealed class FuentesObligatoriasBackofficeTests
{
    private const string Url = Program.Prefijo + BackofficeEndpoints.Grupo + "/fuentes-obligatorias";
    private static readonly string[] CamposDefinicion = ["familia", "tipo_tramite_codigo", "fuentes", "tipo_tramite_etiqueta", "familia_etiqueta"];
    private const string Tipos = Program.Prefijo + "/tipos-tramite";

    // Sin ?empresa= es 200, no EMPRESA_REQUERIDA. Mandar ?empresa= no cambia la respuesta.
    [Fact]
    public async Task Get_y_put_sin_empresa_responden_200()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        using var cliente = api.Cliente(Token(api));

        var get = await cliente.GetAsync(Url, ct);
        var conEmpresa = await cliente.GetAsync(Url + "?empresa=" + Guid.NewGuid(), ct);
        var put = await cliente.PutAsync(Url, Cuerpo("""{ "familia": "TRASPASO", "tipo_tramite_codigo": null, "fuentes": ["SIMIT_PROPIETARIO"] }"""), ct);

        var texto = await get.Content.ReadAsStringAsync(ct);
        var textoConEmpresa = await conEmpresa.Content.ReadAsStringAsync(ct);
        var textoPut = await put.Content.ReadAsStringAsync(ct);
        get.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        texto.Should().NotContain("EMPRESA_REQUERIDA");
        conEmpresa.StatusCode.Should().Be(HttpStatusCode.OK, textoConEmpresa);
        textoConEmpresa.Should().Be(texto);
        put.StatusCode.Should().Be(HttpStatusCode.OK, textoPut);

        var definiciones = JsonDocument.Parse(texto).RootElement.GetProperty("definiciones").EnumerateArray().ToList();
        definiciones.Select(d => (d.GetProperty("familia").GetString(), d.GetProperty("tipo_tramite_codigo").GetString()))
            .Should().Equal(
                ("TRASPASO", null),
                (null, "TRASPASO_STANDARD"),
                (null, "TRASPASO_TRANSFERENCIA_DE_DOMINIO"),
                (null, "TRASPASO_UNILATERAL"));
        Fuentes(definiciones[0]).Should().Equal("SIMIT_PROPIETARIO");
        Fuentes(definiciones[1]).Should().Equal("SIMIT_PROPIETARIO");
        Fuentes(definiciones[2]).Should().Equal("SIMIT_PROPIETARIO");
        Fuentes(definiciones[3]).Should().BeEmpty();
        definiciones.Should().OnlyContain(d => d.EnumerateObject().Select(p => p.Name).SequenceEqual(CamposDefinicion));
    }

    // Fuera de la política diagnostico.backoffice: 403, sin llegar a pedir empresa.
    [Fact]
    public async Task Fuera_de_la_politica_backoffice_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        var deOtraCompania = api.Token(permisos: [Permisos.BackofficeVer], roles: ["backoffice_diagnostico"], tipoEmpresa: "RENTING");
        var flitSinPermiso = api.Token(permisos: [Permisos.DiagnosticosVer], roles: ["admin_diagnostico"], tipoEmpresa: "FLIT");

        foreach (var token in new[] { deOtraCompania, flitSinPermiso, api.Token(permisos: [Permisos.BackofficeVer], roles: ["backoffice_diagnostico"]) })
        {
            using var cliente = api.Cliente(token);
            (await cliente.GetAsync(Url, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await cliente.PutAsync(Url, Cuerpo("""{ "familia": "TRASPASO", "tipo_tramite_codigo": null, "fuentes": ["SIMIT_PROPIETARIO"] }"""), ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    // SIMIT_VEHICULO en el unilateral se rechaza y no se escribe.
    [Fact]
    public async Task Simit_en_el_unilateral_responde_SIMIT_PROHIBIDO_EN_UNILATERAL()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);

        var respuesta = await api.Cliente(Token(api)).PutAsync(Url, Cuerpo("""
            { "familia": null, "tipo_tramite_codigo": "TRASPASO_UNILATERAL", "fuentes": ["SIMIT_VEHICULO"] }
            """), ct);

        var json = await CuerpoDe(respuesta, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, json.GetRawText());
        json.GetProperty("code").GetString().Should().Be(RechazoGuardadoFuentes.SimitProhibidoEnUnilateral);
        json.GetProperty("campo").GetString().Should().Be(RechazoGuardadoFuentes.CampoFuentes);
        (await Lineas(api, "TRASPASO_UNILATERAL", ct)).Should().BeEmpty();
    }

    // Quitar SIMIT_PROPIETARIO del ordinario se rechaza y el piso sigue guardado.
    [Fact]
    public async Task Quitar_el_piso_del_ordinario_responde_PISO_NO_DISPENSABLE()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);

        var respuesta = await api.Cliente(Token(api)).PutAsync(Url, Cuerpo("""
            { "familia": null, "tipo_tramite_codigo": "TRASPASO_STANDARD", "fuentes": ["SIMIT_VEHICULO"] }
            """), ct);

        var json = await CuerpoDe(respuesta, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, json.GetRawText());
        json.GetProperty("code").GetString().Should().Be(RechazoGuardadoFuentes.PisoNoDispensable);
        json.GetProperty("campo").GetString().Should().Be(RechazoGuardadoFuentes.CampoFuentes);
        (await Lineas(api, "TRASPASO_STANDARD", ct)).Should().Equal("SIMIT_PROPIETARIO");
    }

    // Agregar SIMIT_VEHICULO al ordinario se publica además del piso, con la copia de Trámites todavía vigente.
    [Fact]
    public async Task Agregar_simit_vehiculo_al_ordinario_se_publica_con_la_copia_de_tramites_vigente()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var token = Token(api);
        using var alta = api.Cliente(api.Token(permisos: [Permisos.DiagnosticosCrear]));
        (await alta.GetAsync(Tipos, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var idAntes = await IdDe(api, "TRASPASO_STANDARD", ct);

        var put = await api.Cliente(token).PutAsync(Url, Cuerpo("""
            { "familia": null, "tipo_tramite_codigo": "TRASPASO_STANDARD", "fuentes": ["SIMIT_PROPIETARIO", "SIMIT_VEHICULO", "SIMIT_PROPIETARIO"] }
            """), ct);
        var guardado = await CuerpoDe(put, ct);
        put.StatusCode.Should().Be(HttpStatusCode.OK, guardado.GetRawText());
        Codigos(guardado.GetProperty("fuentes")).Should().Equal("SIMIT_VEHICULO", "SIMIT_PROPIETARIO");

        var tipos = await CuerpoDe(await alta.GetAsync(Tipos, ct), ct);
        api.Catalogos.Llamadas.Should().Be(1, "la copia de código, nombre y familia sigue vigente");
        FuentesDe(tipos, "TRASPASO_STANDARD").Should().Equal("SIMIT_VEHICULO", "SIMIT_PROPIETARIO");
        FuentesDe(tipos, "TRASPASO_UNILATERAL").Should().BeEmpty();
        (await IdDe(api, "TRASPASO_STANDARD", ct)).Should().Be(idAntes, "la cabecera que ya existe se conserva");
        (await Autor(api, "TRASPASO_STANDARD", ct)).Should().Be(Sub(token));
    }

    // RUNT no se guarda en este catálogo.
    [Fact]
    public async Task Incluir_runt_responde_FUENTE_RUNT_NO_CONFIGURABLE()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);

        var respuesta = await api.Cliente(Token(api)).PutAsync(Url, Cuerpo("""
            { "familia": null, "tipo_tramite_codigo": "TRASPASO_STANDARD", "fuentes": ["RUNT", "SIMIT_PROPIETARIO"] }
            """), ct);

        var json = await CuerpoDe(respuesta, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, json.GetRawText());
        json.GetProperty("code").GetString().Should().Be(RechazoGuardadoFuentes.FuenteRuntNoConfigurable);
        json.GetProperty("campo").GetString().Should().Be(RechazoGuardadoFuentes.CampoFuentes);
        (await Lineas(api, "TRASPASO_STANDARD", ct)).Should().Equal("SIMIT_PROPIETARIO");
    }

    // Familia y tipo a la vez es forma, antes que una fuente desconocida.
    [Fact]
    public async Task Familia_y_tipo_a_la_vez_responde_DEFINICION_AMBIGUA()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        using var cliente = api.Cliente(Token(api));

        var ambos = await cliente.PutAsync(Url, Cuerpo("""
            { "familia": "TRASPASO", "tipo_tramite_codigo": "TRASPASO_STANDARD", "fuentes": ["NO_EXISTE"] }
            """), ct);
        var ninguno = await cliente.PutAsync(Url, Cuerpo("""{ "familia": null, "tipo_tramite_codigo": null, "fuentes": [] }"""), ct);

        foreach (var respuesta in new[] { ambos, ninguno })
        {
            var json = await CuerpoDe(respuesta, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, json.GetRawText());
            json.GetProperty("code").GetString().Should().Be(RechazoGuardadoFuentes.DefinicionAmbigua);
            json.GetProperty("campo").GetString().Should().Be(RechazoGuardadoFuentes.CampoFamilia);
        }

        (await CabecerasDe(api, ct)).Should().Be(4, "con el error no se escribe ninguna cabecera");
    }

    // fuentes: [] deja la cabecera (vacío explícito) y una cabecera nueva nace con uuid v7.
    [Fact]
    public async Task Fuentes_vacias_dejan_la_cabecera()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        using var cliente = api.Cliente(Token(api));

        var creado = await cliente.PutAsync(Url, Cuerpo("""
            { "familia": null, "tipo_tramite_codigo": "MATRICULA_INICIAL", "fuentes": [] }
            """), ct);
        creado.StatusCode.Should().Be(HttpStatusCode.OK, await creado.Content.ReadAsStringAsync(ct));
        var id = await IdDe(api, "MATRICULA_INICIAL", ct);
        id.Version.Should().Be(7);

        var otraVez = await cliente.PutAsync(Url, Cuerpo("""
            { "familia": null, "tipo_tramite_codigo": "MATRICULA_INICIAL", "fuentes": [] }
            """), ct);
        otraVez.StatusCode.Should().Be(HttpStatusCode.OK, await otraVez.Content.ReadAsStringAsync(ct));
        (await IdDe(api, "MATRICULA_INICIAL", ct)).Should().Be(id, "el segundo vacío conserva la cabecera");

        var lista = await CuerpoDe(await cliente.GetAsync(Url, ct), ct);
        var matricula = lista.GetProperty("definiciones").EnumerateArray().Single(d => d.GetProperty("tipo_tramite_codigo").GetString() == "MATRICULA_INICIAL");
        Codigos(matricula.GetProperty("fuentes")).Should().BeEmpty();
        lista.GetProperty("definiciones").EnumerateArray().Should().Contain(d => d.GetProperty("tipo_tramite_codigo").GetString() == "TRASPASO_UNILATERAL");
    }

    // El cuerpo que no es un objeto JSON es 400, antes de mirar el negocio.
    [Fact]
    public async Task Cuerpo_que_no_es_objeto_responde_CUERPO_INVALIDO()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);

        var respuesta = await api.Cliente(Token(api)).PutAsync(Url, Cuerpo("[]"), ct);

        var json = await CuerpoDe(respuesta, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, json.GetRawText());
        json.GetProperty("code").GetString().Should().Be("CUERPO_INVALIDO");
    }

    // Una fuente que no está en el enum es 422, después de aceptar el alcance.
    [Fact]
    public async Task Fuente_desconocida_responde_FUENTE_DESCONOCIDA()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);

        var respuesta = await api.Cliente(Token(api)).PutAsync(Url, Cuerpo("""
            { "familia": null, "tipo_tramite_codigo": "TRASPASO_STANDARD", "fuentes": ["NO_EXISTE"] }
            """), ct);

        var json = await CuerpoDe(respuesta, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, json.GetRawText());
        json.GetProperty("code").GetString().Should().Be("FUENTE_DESCONOCIDA");
        json.GetProperty("campo").GetString().Should().Be(RechazoGuardadoFuentes.CampoFuentes);
    }

    private static string Token(ApiDePrueba api) =>
        api.Token(permisos: [Permisos.BackofficeVer], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT");

    private static async Task<JsonElement> CuerpoDe(HttpResponseMessage respuesta, CancellationToken ct) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct)).RootElement;

    private static IEnumerable<string?> Fuentes(JsonElement definicion) => Codigos(definicion.GetProperty("fuentes"));

    private static IEnumerable<string?> FuentesDe(JsonElement tipos, string codigo) =>
        Codigos(tipos.GetProperty("tipos").EnumerateArray().Single(t => t.GetProperty("codigo").GetString() == codigo).GetProperty("fuentesObligatorias"));

    private static IEnumerable<string?> Codigos(JsonElement arreglo) => arreglo.EnumerateArray().Select(e => e.GetString());

    private static async Task<List<string>> Lineas(ApiDePrueba api, string tipo, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var id = await db.Set<DefinicionFuentesObligatorias>().AsNoTracking()
            .Where(d => d.TipoTramiteCodigo == tipo)
            .Select(d => d.Id)
            .SingleAsync(ct);
        var fuentes = await db.Set<FuenteDefinicionObligatoria>().AsNoTracking()
            .Where(l => l.DefinicionId == id)
            .Select(l => l.Fuente)
            .ToListAsync(ct);
        return fuentes.OrderBy(f => f).Select(Flit.Diagnostico.Api.Persistence.Configuraciones.CodigosDeEnum.Codigo).ToList();
    }

    private static async Task<Guid> IdDe(ApiDePrueba api, string tipo, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        return await db.Set<DefinicionFuentesObligatorias>().AsNoTracking()
            .Where(d => d.TipoTramiteCodigo == tipo)
            .Select(d => d.Id)
            .SingleAsync(ct);
    }

    private static async Task<Guid?> Autor(ApiDePrueba api, string tipo, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        return await db.Set<DefinicionFuentesObligatorias>().AsNoTracking()
            .Where(d => d.TipoTramiteCodigo == tipo)
            .Select(d => d.ActualizadoPor)
            .SingleAsync(ct);
    }

    private static async Task<int> CabecerasDe(ApiDePrueba api, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        return await db.Set<DefinicionFuentesObligatorias>().CountAsync(ct);
    }

    private static Guid Sub(string token)
    {
        var carga = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        carga = carga.PadRight(carga.Length + ((4 - (carga.Length % 4)) % 4), '=');
        return JsonDocument.Parse(Convert.FromBase64String(carga)).RootElement.GetProperty("sub").GetGuid();
    }
}
