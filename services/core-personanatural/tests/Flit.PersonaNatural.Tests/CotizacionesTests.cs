using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.PersonaNatural.Api;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>Fachada de core-api de prueba: cuenta las llamadas y devuelve lo que se le configure.</summary>
internal sealed class FachadaFalsa : IFachadaTramites
{
    public int LlamadasOrganismos;
    public int LlamadasTipos;
    public int LlamadasCotizar;
    public string? ErrorCotizar;
    public string? UltimoOrganismoDeTipos;
    public List<string> UltimosAdicionales { get; } = [];

    public Task<IReadOnlyList<OrganismoDto>> ListarOrganismosAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref LlamadasOrganismos);
        return Task.FromResult<IReadOnlyList<OrganismoDto>>([new OrganismoDto(CotizacionesTests.Organismo, "AB1", "Tránsito de Prueba", "Municipio", "Depto", string.Empty)]);
    }

    public Task<IReadOnlyList<TipoTramiteDto>> ListarTiposAsync(string? organismoId, CancellationToken ct)
    {
        Interlocked.Increment(ref LlamadasTipos);
        UltimoOrganismoDeTipos = organismoId;
        return Task.FromResult<IReadOnlyList<TipoTramiteDto>>([new TipoTramiteDto("TRASPASO_STANDARD", "Traspaso", "TRASPASO", 1, true, string.Empty)]);
    }

    public Task<CotizacionDto> CotizarAsync(string tipoCodigo, string organismoId, IReadOnlyCollection<string> adicionales, CancellationToken ct)
    {
        Interlocked.Increment(ref LlamadasCotizar);
        UltimosAdicionales.Clear();
        UltimosAdicionales.AddRange(adicionales);
        if (ErrorCotizar is { } e)
            throw new FachadaException(e, "error de prueba", e == "tipo_no_disponible" ? 404 : 422);
        return Task.FromResult(new CotizacionDto(
            [new LineaCobroDto("DERECHOS_TRANSITO", "Valor del Servicio de tránsito", string.Empty, 185_000), new LineaCobroDto("SERVICIO_FLIT", "Valor de Servicio FLIT", "Uso de la plataforma", 181_000)],
            366_000, "v1", []));
    }
}

/// <summary>Enviador de prueba: guarda lo que se le pidió enviar.</summary>
internal sealed class EnviadorFalso : IEnviadorCotizacion
{
    public List<(string Correo, string Html)> Enviados { get; } = [];
    public bool Acepta = true;

    public Task<bool> EnviarAsync(string correo, string asunto, string html, CancellationToken ct)
    {
        Enviados.Add((correo, html));
        return Task.FromResult(Acepta);
    }
}

/// <summary>Captura todo lo que el servicio escribe en logs, para comprobar que no sale el correo.</summary>
internal sealed class RegistroDeLogs : ILoggerProvider
{
    public List<string> Mensajes { get; } = [];

    public ILogger CreateLogger(string categoryName) => new Registro(this);

    public void Dispose()
    {
    }

    private sealed class Registro(RegistroDeLogs padre) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (padre.Mensajes)
                padre.Mensajes.Add(formatter(state, exception) + exception);
        }
    }
}

/// <summary>
/// HU #14043 (Feature B, Épica #14021) — catálogo, cotización y envío por correo del portal, sobre el host real con la
/// fachada de core-api y el transporte de correo falsos (base efímera propia; sin motor se omite en local y falla en CI).
/// </summary>
public sealed class CotizacionesTests : IAsyncLifetime
{
    public const string Organismo = "0199b7e0-0000-7000-8000-000000000001";
    private const string Cotizar = "/api/v1/persona-natural/cotizaciones";
    private const string EnviarCorreo = "/api/v1/persona-natural/cotizaciones/enviar-correo";
    private const string Correo = "ciudadano.secreto@example.org";

    private readonly FachadaFalsa _fachada = new();
    private readonly EnviadorFalso _enviador = new();
    private readonly RegistroDeLogs _logs = new();
    private string _database = string.Empty;
    private string? _skip;
    private WebApplication? _app;
    private HttpClient _http = null!;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
        if (_skip is null && _database.Length > 0)
        {
            NpgsqlConnection.ClearAllPools();
            var options = new DbContextOptionsBuilder<PersonaNaturalDb>();
            PersonaNaturalDb.Configure(options, _database);
            await using var db = new PersonaNaturalDb(options.Options);
            await db.Database.EnsureDeletedAsync();
        }
    }

    // ── AC1: catálogo ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElCatalogoDeOrganismosYTipos_SaleDeLaFachada_YSeCacheaUnosSegundos()
    {
        if (!await IniciarAsync()) return;
        var ct = TestContext.Current.CancellationToken;

        var primera = await _http.GetAsync("/api/v1/persona-natural/organismos", ct);
        await _http.GetAsync("/api/v1/persona-natural/organismos", ct);
        var tipos = await _http.GetAsync($"/api/v1/persona-natural/tipos-tramite?organismoId={Organismo}", ct);
        await _http.GetAsync($"/api/v1/persona-natural/tipos-tramite?organismoId={Organismo}", ct);

        primera.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(primera)).RootElement[0].GetProperty("nombre").GetString().Should().Be("Tránsito de Prueba");
        (await Json(tipos)).RootElement[0].GetProperty("code").GetString().Should().Be("TRASPASO_STANDARD");
        _fachada.LlamadasOrganismos.Should().Be(1, "la segunda lectura sale de la caché corta");
        _fachada.LlamadasTipos.Should().Be(1);
        _fachada.UltimoOrganismoDeTipos.Should().Be(Organismo);
    }

    [Fact]
    public async Task UnOrganismoQueNoEsUnIdentificador_Responde400SinLlamarALaFachada()
    {
        if (!await IniciarAsync()) return;

        var r = await _http.GetAsync("/api/v1/persona-natural/tipos-tramite?organismoId=no-es-guid", TestContext.Current.CancellationToken);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fachada.LlamadasTipos.Should().Be(0);
    }

    // ── AC2 y AC4: cotización ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaCotizacion_ExigeCaptcha_YDevuelveLasDosLineasYElTotalDeLaFachada()
    {
        if (!await IniciarAsync()) return;
        var ct = TestContext.Current.CancellationToken;
        var cuerpo = new { organismoId = Organismo, tipoCodigo = "TRASPASO_STANDARD", adicionales = new[] { "ENVIO_DOCUMENTO" } };

        var sinCaptcha = await _http.PostAsJsonAsync(Cotizar, cuerpo, ct);
        var ok = await PostAsync(Cotizar, cuerpo, captcha: true);

        sinCaptcha.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Codigo(sinCaptcha)).Should().Be("captcha_required");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = (await Json(ok)).RootElement;
        json.GetProperty("lineas").GetArrayLength().Should().Be(2);
        json.GetProperty("lineas")[1].GetProperty("subtitulo").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("totalCop").GetInt64().Should().Be(366_000);
        _fachada.UltimosAdicionales.Should().Equal("ENVIO_DOCUMENTO");
    }

    [Theory]
    [InlineData("tipo_no_disponible", HttpStatusCode.NotFound)]
    [InlineData("tipo_no_habilitado", HttpStatusCode.UnprocessableEntity)]
    [InlineData("tarifa_no_vigente", HttpStatusCode.UnprocessableEntity)]
    [InlineData("organismo_no_habilitado", HttpStatusCode.UnprocessableEntity)]
    public async Task LosErroresDeLaFachada_SeTraducenConSuCodigoEstable(string codigo, HttpStatusCode estado)
    {
        if (!await IniciarAsync()) return;
        _fachada.ErrorCotizar = codigo;

        var r = await PostAsync(Cotizar, new { organismoId = Organismo, tipoCodigo = "X" }, captcha: true);

        r.StatusCode.Should().Be(estado);
        (await Codigo(r)).Should().Be(codigo);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"organismoId":"no-guid","tipoCodigo":"X"}""")]
    [InlineData("""{"organismoId":"0199b7e0-0000-7000-8000-000000000001","tipoCodigo":""}""")]
    [InlineData("""{"organismoId":"0199b7e0-0000-7000-8000-000000000001","tipoCodigo":"X","adicionales":["A","B","C","D","E"]}""")]
    public async Task UnaCotizacionMalFormada_Responde400_YNoLlegaALaFachada(string json)
    {
        if (!await IniciarAsync()) return;
        using var cuerpo = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var peticion = new HttpRequestMessage(HttpMethod.Post, Cotizar) { Content = cuerpo };
        peticion.Headers.Add(AbusoEndpoints.CabeceraCaptcha, CaptchaFalso.TokenValido);

        var r = await _http.SendAsync(peticion, TestContext.Current.CancellationToken);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fachada.LlamadasCotizar.Should().Be(0);
    }

    [Fact]
    public async Task SinFachadaConfigurada_ElCatalogoResponde503ConCodigo_YElServicioArranca()
    {
        if (!await IniciarAsync(conFalsos: false)) return;

        var r = await _http.GetAsync("/api/v1/persona-natural/organismos", TestContext.Current.CancellationToken);

        r.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Codigo(r)).Should().Be("servicio_no_disponible");
    }

    // ── AC3: cotización por correo ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnviarPorCorreo_ArmaElCorreoConLaCotizacionDeLaFachada_YNoSeEscribeElCorreoEnLogs()
    {
        if (!await IniciarAsync()) return;

        var r = await PostAsync(EnviarCorreo, Cuerpo(Correo), captcha: true, idempotencia: "k-1");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var enviado = _enviador.Enviados.Should().ContainSingle().Which;
        enviado.Correo.Should().Be(Correo);
        enviado.Html.Should().Contain("$ 366.000 COP").And.Contain("Valor de Servicio FLIT").And.NotContain("secreto");
        lock (_logs.Mensajes)
            _logs.Mensajes.Should().NotContain(m => m.Contains("ciudadano.secreto", StringComparison.OrdinalIgnoreCase), "el correo no se loguea");
    }

    [Fact]
    public async Task EnviarPorCorreo_ExigeCaptchaIdempotencyKeyYCorreoValido()
    {
        if (!await IniciarAsync()) return;

        var sinCaptcha = await PostAsync(EnviarCorreo, Cuerpo(Correo), captcha: false, idempotencia: "k");
        var sinClave = await PostAsync(EnviarCorreo, Cuerpo(Correo), captcha: true, idempotencia: null);
        var correoMalo = await PostAsync(EnviarCorreo, Cuerpo("no-es-correo"), captcha: true, idempotencia: "k");

        sinCaptcha.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Codigo(sinClave)).Should().Be(CotizacionesEndpoints.CodigoIdempotenciaRequerida);
        (await Codigo(correoMalo)).Should().Be("correo_invalido");
        _enviador.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task LaMismaIdempotencyKey_NoReenviaElCorreo()
    {
        if (!await IniciarAsync()) return;

        await PostAsync(EnviarCorreo, Cuerpo(Correo), captcha: true, idempotencia: "misma");
        var repetido = await PostAsync(EnviarCorreo, Cuerpo(Correo), captcha: true, idempotencia: "misma");

        repetido.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(repetido)).RootElement.GetProperty("repetido").GetBoolean().Should().BeTrue();
        _enviador.Enviados.Should().HaveCount(1);
    }

    [Fact]
    public async Task ElLimiteEstrictoPorCorreo_Responde429_YOtroCorreoNoSeAfecta()
    {
        if (!await IniciarAsync(("Cotizaciones:LimiteCorreoPorVentana", "2"))) return;

        for (var i = 0; i < 2; i++)
            (await PostAsync(EnviarCorreo, Cuerpo(Correo), captcha: true, idempotencia: $"a{i}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var excedido = await PostAsync(EnviarCorreo, Cuerpo(Correo.ToUpperInvariant()), captcha: true, idempotencia: "a2");
        var otro = await PostAsync(EnviarCorreo, Cuerpo("otra.persona@example.org"), captcha: true, idempotencia: "b0");

        excedido.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await Codigo(excedido)).Should().Be(CotizacionesEndpoints.CodigoLimiteCorreo);
        otro.StatusCode.Should().Be(HttpStatusCode.OK);
        _enviador.Enviados.Should().HaveCount(3);
    }

    [Fact]
    public async Task SiElTransporteNoAcepta_Responde502_YLaMismaClavePuedeReintentarse()
    {
        if (!await IniciarAsync()) return;
        _enviador.Acepta = false;

        var fallo = await PostAsync(EnviarCorreo, Cuerpo(Correo), captcha: true, idempotencia: "reintento");
        _enviador.Acepta = true;
        var ok = await PostAsync(EnviarCorreo, Cuerpo(Correo), captcha: true, idempotencia: "reintento");

        fallo.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await Codigo(fallo)).Should().Be(CotizacionesEndpoints.CodigoEnvioFallido);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(ok)).RootElement.GetProperty("repetido").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void ElCorreoArmado_CodificaTodoTexto_YNoLlevaDatosPersonales()
    {
        var html = CotizacionesService.ArmarHtml(new CotizacionDto([new LineaCobroDto("X", "<script>alert(1)</script>", "a & b", 1_000)], 1_000, "v", []));

        html.Should().NotContain("<script>").And.Contain("&lt;script&gt;").And.Contain("a &amp; b").And.Contain("$ 1.000 COP");
    }

    // ── Infraestructura de la prueba ─────────────────────────────────────────────────────────────────────────────────

    private static object Cuerpo(string correo) => new { organismoId = Organismo, tipoCodigo = "TRASPASO_STANDARD", correo };

    private async Task<HttpResponseMessage> PostAsync(string ruta, object cuerpo, bool captcha, string? idempotencia = null)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo) };
        if (captcha)
            peticion.Headers.Add(AbusoEndpoints.CabeceraCaptcha, CaptchaFalso.TokenValido);
        if (idempotencia is not null)
            peticion.Headers.Add(CotizacionesEndpoints.CabeceraIdempotencia, idempotencia);
        return await _http.SendAsync(peticion, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonDocument> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    private static async Task<string?> Codigo(HttpResponseMessage r)
    {
        using var json = await Json(r);
        return json.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
    }

    private Task<bool> IniciarAsync(params (string Clave, string Valor)[] ajustes) => IniciarAsync(true, ajustes);

    private async Task<bool> IniciarAsync(bool conFalsos, params (string Clave, string Valor)[] ajustes)
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_pncot_{Guid.NewGuid():N}"[..30] }.ConnectionString;
        try
        {
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(_database).Database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException ex) when (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            _skip = $"Postgres no alcanzable: {ex.Message}";
            Assert.Skip(_skip);
            return false;
        }

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Servicio"] = _database,
            ["Platform:ServiceClient:ClientSecret"] = "secreto-de-prueba",
            ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
            ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
            ["Platform:Auth:Issuers:0"] = "https://hub.prueba/",
            ["Platform:Messaging:ConnectionString"] = "amqp://flit:x@127.0.0.1:1/",
            [ServicioSettings.GrpcPortKey] = "5996",
            ["Pii:ClaveHmac"] = "clave-de-prueba-de-al-menos-32-caracteres!",
            ["Captcha:SecretKey"] = "secreto-de-captcha",
            ["Pii:Cifrado:Llaves:1"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
            ["Abuso:LimiteIpPorMinuto"] = "1000",
        };
        foreach (var (clave, valor) in ajustes)
            settings[clave] = valor;

        _app = Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(settings);
            b.Services.AddSingleton<IProveedorCaptcha, CaptchaFalso>();
            b.Logging.AddProvider(_logs);
            if (conFalsos)
            {
                b.Services.AddSingleton<IFachadaTramites>(_fachada);
                b.Services.AddSingleton<IEnviadorCotizacion>(_enviador);
            }
        });
        await _app.StartAsync();
        await Program.MigrateAsync(_app);
        _http = _app.GetTestClient();
        return true;
    }
}
