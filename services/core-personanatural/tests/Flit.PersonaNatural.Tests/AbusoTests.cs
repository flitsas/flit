using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.PersonaNatural.Api;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>Captcha de prueba: solo acepta <see cref="TokenValido"/>.</summary>
internal sealed class CaptchaFalso : IProveedorCaptcha
{
    public const string TokenValido = "captcha-ok";

    public Task<bool> VerificarAsync(string token, string? ip, CancellationToken ct) => Task.FromResult(token == TokenValido);
}

/// <summary>
/// HU #14036 (Feature #14022, Épica #14021) — protección anti-abuso del portal público: captcha, límite por IP, límite por
/// cédula y tope global, sobre el host real y una base migrada (base efímera propia; sin motor se omite en local y falla en CI).
/// </summary>
public sealed class AbusoTests : IAsyncLifetime
{
    private const string Cedula = "1020456789";

    private readonly RelojFalso _reloj = new(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero));
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
            await using var db = NuevaDb();
            await db.Database.EnsureDeletedAsync();
        }
    }

    // ── AC1: captcha obligatorio ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinCaptcha_ElEndpointPublicoResponde400_YNoEjecutaLaAccion()
    {
        if (!await IniciarAsync()) return;

        var sin = await _http.PostAsJsonAsync(Reanudar, new ReanudarRequest("PN-1", Cedula), TestContext.Current.CancellationToken);
        var malo = await PostConCaptchaAsync("token-falso");
        var bueno = await PostConCaptchaAsync(CaptchaFalso.TokenValido);

        sin.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodigoAsync(sin)).Should().Be("captcha_required");
        malo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodigoAsync(malo)).Should().Be("captcha_required");
        bueno.StatusCode.Should().Be(HttpStatusCode.NotFound, "con captcha válido la acción corre y responde lo suyo");

        await using var db = NuevaDb();
        (await db.Set<IntentoReanudar>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1, "solo la petición con captcha válido llegó a reanudar");
    }

    // ── AC2: límite por IP ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaIPQueSuperaElLimite_Recibe429ConRetryAfter()
    {
        if (!await IniciarAsync(("Abuso:LimiteIpPorMinuto", "3"))) return;

        for (var i = 0; i < 3; i++)
            (await PostConCaptchaAsync(CaptchaFalso.TokenValido)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        var rechazada = await PostConCaptchaAsync(CaptchaFalso.TokenValido);

        rechazada.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await CodigoAsync(rechazada)).Should().Be("rate_limited");
        rechazada.Headers.RetryAfter.Should().NotBeNull();
        (await _http.GetAsync("/health", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK, "la salud no cuenta ni se limita");
    }

    // ── AC3: límite por cédula ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaCedulaQueSuperaElLimitePorHora_SeRechazaYQuedaAuditada_SinGuardarElNumero()
    {
        if (!await IniciarAsync(("Abuso:LimiteCedulaPorHora", "3"))) return;

        for (var i = 0; i < 3; i++)
            (await ReservarAsync(Cedula)).Should().Be(ResultadoCuota.Permitida);
        var cuarta = await ReservarAsync("1.020.456.789");
        var otra = await ReservarAsync("79555111");

        cuarta.Should().Be(ResultadoCuota.CedulaExcedida, "la cédula con puntos es la misma");
        otra.Should().Be(ResultadoCuota.Permitida, "el tope es por documento");

        await using var db = NuevaDb();
        (await db.Set<AccesoPiiLog>().CountAsync(a => a.Accion == CuotaService.AccionCedulaExcedida, TestContext.Current.CancellationToken)).Should().Be(1);
        var claves = await db.Set<CuotaConsulta>().Select(c => c.Clave).ToListAsync(TestContext.Current.CancellationToken);
        claves.Should().NotContain(c => c.Contains(Cedula) || c.Contains("79555111"), "la cédula solo existe como huella");

        _reloj.Avanzar(TimeSpan.FromHours(1));
        (await ReservarAsync(Cedula)).Should().Be(ResultadoCuota.Permitida, "la ventana es de una hora");
    }

    // ── AC4: tope global ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AlAlcanzarElTopeGlobal_LasConsultasNuevasSePausan_YElRestoSigueOperando()
    {
        if (!await IniciarAsync(("Abuso:TopeGlobalDiario", "3"))) return;

        for (var i = 0; i < 3; i++)
            (await ReservarAsync($"100{i}")).Should().Be(ResultadoCuota.Permitida);
        (await ReservarAsync("2000")).Should().Be(ResultadoCuota.TopeGlobal);
        (await ReservarAsync("2001")).Should().Be(ResultadoCuota.TopeGlobal);

        (await _http.GetAsync("/health", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        var reanudar = await PostConCaptchaAsync(CaptchaFalso.TokenValido);
        reanudar.StatusCode.Should().Be(HttpStatusCode.NotFound, "reanudar no es una consulta: sigue funcionando");

        await using var db = NuevaDb();
        (await db.Set<AccesoPiiLog>().CountAsync(a => a.Accion == CuotaService.AccionTopeGlobal, TestContext.Current.CancellationToken))
            .Should().Be(1, "el aviso se audita una sola vez, no por cada rechazo");

        _reloj.Avanzar(TimeSpan.FromDays(1));
        (await ReservarAsync("3000")).Should().Be(ResultadoCuota.Permitida, "el tope es diario");
    }

    [Fact]
    public async Task ElFiltroDeConsultas_TraduceLosTopesAHttp_429PorCedulaY503PorTopeGlobal()
    {
        if (!await IniciarAsync(("Abuso:LimiteCedulaPorHora", "1"), ("Abuso:TopeGlobalDiario", "2"), configurar: app =>
                app.MapGet("/prueba-consulta", (HttpContext http) => Results.Ok())
                    .ConLimiteDeConsultas(c => c.HttpContext.Request.Query["ced"].ToString()))) return;

        (await _http.GetAsync("/prueba-consulta?ced=111", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        var porCedula = await _http.GetAsync("/prueba-consulta?ced=111", TestContext.Current.CancellationToken);
        (await _http.GetAsync("/prueba-consulta?ced=222", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        var global = await _http.GetAsync("/prueba-consulta?ced=333", TestContext.Current.CancellationToken);
        var sinCedula = await _http.GetAsync("/prueba-consulta", TestContext.Current.CancellationToken);

        porCedula.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await CodigoAsync(porCedula)).Should().Be("cedula_rate_limited");
        global.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CodigoAsync(global)).Should().Be("consultas_no_disponibles");
        sinCedula.StatusCode.Should().Be(HttpStatusCode.OK, "sin documento no hay nada que contar");
    }

    // ── Captcha: proveedor intercambiable por configuración (AC5) ───────────────────────────────────────────────────

    [Theory]
    [InlineData("turnstile", "https://challenges.cloudflare.com/turnstile/v0/siteverify")]
    [InlineData("recaptcha", "https://www.google.com/recaptcha/api/siteverify")]
    [InlineData("RECAPTCHA", "https://www.google.com/recaptcha/api/siteverify")]
    public async Task ElProveedorSeEligePorConfiguracion_YValidaContraSuURL(string proveedor, string urlEsperada)
    {
        var manejador = new ManejadorFalso(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true }) });
        var captcha = NuevoProveedor(manejador, new CaptchaOptions { Proveedor = proveedor, SecretKey = "s3cret" });

        (await captcha.VerificarAsync("tk", "10.0.0.1", TestContext.Current.CancellationToken)).Should().BeTrue();

        manejador.Url.Should().Be(urlEsperada);
        manejador.Cuerpo.Should().Contain("secret=s3cret").And.Contain("response=tk").And.Contain("remoteip=10.0.0.1");
    }

    [Fact]
    public async Task ElCaptchaSeNiega_SiElProveedorRechazaFallaOSeCae()
    {
        var rechaza = NuevoProveedor(new ManejadorFalso(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = false }) }),
            new CaptchaOptions { SecretKey = "x" });
        var error500 = NuevoProveedor(new ManejadorFalso(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)), new CaptchaOptions { SecretKey = "x" });
        var caido = NuevoProveedor(new ManejadorFalso(_ => throw new HttpRequestException("sin red")), new CaptchaOptions { SecretKey = "x" });

        (await rechaza.VerificarAsync("tk", null, TestContext.Current.CancellationToken)).Should().BeFalse();
        (await error500.VerificarAsync("tk", null, TestContext.Current.CancellationToken)).Should().BeFalse();
        (await caido.VerificarAsync("tk", null, TestContext.Current.CancellationToken)).Should().BeFalse("sin respuesta del proveedor se niega");
        (await rechaza.VerificarAsync("", null, TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Theory]
    [InlineData("desconocido", "x", false, "*Proveedor de captcha desconocido*", false)]
    [InlineData("turnstile", "", false, "*Captcha__SecretKey*", false)]
    [InlineData("deshabilitado", "", false, "*solo se permite en Development*", true)]
    [InlineData("deshabilitado", "", true, "*PermitirDeshabilitado=true explícito*", false)]
    public void UnCaptchaMalConfigurado_ImpideArrancar(string proveedor, string secreto, bool development, string mensaje, bool permitir)
    {
        var config = Config(proveedor, secreto, permitir);

        var act = () => CaptchaOptions.Validar(config, development);

        act.Should().Throw<InvalidOperationException>().WithMessage(mensaje);
    }

    [Theory]
    [InlineData("turnstile", "s", false, false)]
    [InlineData("recaptcha", "s", false, false)]
    [InlineData("deshabilitado", "", true, true)]
    public void UnCaptchaBienConfigurado_Arranca(string proveedor, string secreto, bool development, bool permitir)
    {
        var act = () => CaptchaOptions.Validar(Config(proveedor, secreto, permitir), development);

        act.Should().NotThrow();
    }

    // ── Infraestructura de la prueba ─────────────────────────────────────────────────────────────────────────────────

    private const string Reanudar = $"{SesionEndpoints.Base}/sesiones/reanudar";

    private async Task<bool> IniciarAsync(params (string Clave, string Valor)[] ajustes) => await IniciarAsync(ajustes, null);

    private async Task<bool> IniciarAsync((string Clave, string Valor) a, (string Clave, string Valor) b, Action<WebApplication> configurar) =>
        await IniciarAsync([a, b], configurar);

    private async Task<bool> IniciarAsync((string Clave, string Valor) a, (string Clave, string Valor) b, (string Clave, string Valor) c, Action<WebApplication> configurar) =>
        await IniciarAsync([a, b, c], configurar);

    private async Task<bool> IniciarAsync((string Clave, string Valor)[] ajustes, Action<WebApplication>? configurar)
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_pnabu_{Guid.NewGuid():N}"[..30] }.ConnectionString;
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
            [ServicioSettings.GrpcPortKey] = "5997",
            ["Pii:ClaveHmac"] = "clave-de-prueba-de-al-menos-32-caracteres!",
            ["Captcha:SecretKey"] = "secreto-de-captcha",
            ["Pii:Cifrado:Llaves:1"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
        };
        foreach (var (clave, valor) in ajustes)
            settings[clave] = valor;

        _app = Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(settings);
            b.Services.AddSingleton<TimeProvider>(_reloj);
            b.Services.AddSingleton<IProveedorCaptcha, CaptchaFalso>();
        });
        configurar?.Invoke(_app);
        await _app.StartAsync();
        await Program.MigrateAsync(_app);
        _http = _app.GetTestClient();
        return true;
    }

    private async Task<ResultadoCuota> ReservarAsync(string cedula)
    {
        using var scope = _app!.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CuotaService>().ReservarConsultaAsync(cedula, null, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> PostConCaptchaAsync(string token)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, Reanudar) { Content = JsonContent.Create(new ReanudarRequest("PN-1", Cedula)) };
        peticion.Headers.Add(AbusoEndpoints.CabeceraCaptcha, token);
        return await _http.SendAsync(peticion, TestContext.Current.CancellationToken);
    }

    private PersonaNaturalDb NuevaDb()
    {
        var options = new DbContextOptionsBuilder<PersonaNaturalDb>();
        PersonaNaturalDb.Configure(options, _database);
        return new PersonaNaturalDb(options.Options);
    }

    private static async Task<string?> CodigoAsync(HttpResponseMessage respuesta)
    {
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return cuerpo.TryGetProperty("code", out var codigo) ? codigo.GetString() : null;
    }

    private static IConfiguration Config(string proveedor, string secreto, bool permitir = false) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Captcha:Proveedor"] = proveedor,
        ["Captcha:SecretKey"] = secreto,
        ["Captcha:PermitirDeshabilitado"] = permitir ? "true" : "false",
    }).Build();

    private static ProveedorCaptchaHttp NuevoProveedor(ManejadorFalso manejador, CaptchaOptions opciones) =>
        new(new HttpClient(manejador), Microsoft.Extensions.Options.Options.Create(opciones), Microsoft.Extensions.Logging.Abstractions.NullLogger<ProveedorCaptchaHttp>.Instance);

    private sealed class ManejadorFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public string? Url { get; private set; }

        public string? Cuerpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri?.ToString();
            Cuerpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }

    private sealed class RelojFalso(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _ahora = inicio;

        public override DateTimeOffset GetUtcNow() => _ahora;

        public void Avanzar(TimeSpan tiempo) => _ahora += tiempo;
    }
}
