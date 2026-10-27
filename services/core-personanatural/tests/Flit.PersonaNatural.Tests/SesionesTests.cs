using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.PersonaNatural.Api;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14035 (Feature #14022, Épica #14021, ADR-0075) — sesión efímera por trámite y reanudación con radicado y cédula,
/// de punta a punta sobre el host real y una base migrada. Usa una base efímera propia en el Postgres de
/// <c>ConnectionStrings__Core</c> (sin motor: se omite en local y falla en CI).
/// </summary>
public sealed class SesionesTests : IAsyncLifetime
{
    private const string ClaveHmac = "clave-de-prueba-de-al-menos-32-caracteres!";
    private const string Cedula = "1020456789";
    private const string Radicado = "PN-2026-000123";

    private readonly RelojFalso _reloj = new(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero));
    private string _database = string.Empty;
    private string? _skip;
    private WebApplication? _app;
    private HttpClient _http = null!;

    public async ValueTask InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_pnses_{Guid.NewGuid():N}"[..30] }.ConnectionString;
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
            return;
        }

        _app = Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(Settings());
            b.Services.AddSingleton<TimeProvider>(_reloj);
            b.Services.AddSingleton<IProveedorCaptcha, CaptchaFalso>();
        });
        await _app.StartAsync();
        await Program.MigrateAsync(_app);
        _http = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
        if (_skip is null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var db = new PersonaNaturalDb(new DbContextOptionsBuilder<PersonaNaturalDb>().UseNpgsql(_database).Options);
            await db.Database.EnsureDeletedAsync();
        }
    }

    // ── Emisión y validación del token ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AlIniciar_ElTokenEsOpaco_SoloSeGuardaSuHash_YVive60Minutos()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var solicitud = await SembrarSolicitudAsync();

        var emitida = await EnScopeAsync(s => s.IniciarAsync(solicitud, ipHash: null, TestContext.Current.CancellationToken));

        emitida.Token.Length.Should().BeGreaterThanOrEqualTo(43, "256 bits en base64 url-safe");
        emitida.Token.Should().MatchRegex("^[A-Za-z0-9_-]+$").And.NotContain(solicitud.ToString());
        emitida.ExpiraEn.Should().Be(_reloj.GetUtcNow().AddMinutes(60));
        await using var db = NuevaDb();
        var fila = await db.Set<SesionTramite>().SingleAsync(TestContext.Current.CancellationToken);
        fila.TokenHash.Should().Equal(PiiHasher.HashToken(emitida.Token)).And.NotEqual(System.Text.Encoding.UTF8.GetBytes(emitida.Token));
    }

    [Fact]
    public async Task UnTokenValido_RenuevaSuVigenciaConCadaUso_PeroNuncaPasaDelTopeAbsoluto()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var solicitud = await SembrarSolicitudAsync();
        var token = (await EnScopeAsync(s => s.IniciarAsync(solicitud, null, TestContext.Current.CancellationToken))).Token;

        _reloj.Avanzar(TimeSpan.FromMinutes(50));
        (await ValidarAsync(token)).Estado.Should().Be(EstadoSesion.Valida);
        _reloj.Avanzar(TimeSpan.FromMinutes(50));
        (await ValidarAsync(token)).Estado.Should().Be(EstadoSesion.Valida, "el uso a los 50 min la renovó 60 min más");

        // Se sigue usando cada hora hasta rozar las 24 h: el tope absoluto corta aunque haya actividad.
        for (var i = 0; i < 21; i++)
        {
            _reloj.Avanzar(TimeSpan.FromMinutes(55));
            (await ValidarAsync(token)).Estado.Should().Be(EstadoSesion.Valida);
        }

        _reloj.Avanzar(TimeSpan.FromHours(2));
        (await ValidarAsync(token)).Estado.Should().Be(EstadoSesion.Vencida, "pasaron más de 24 h desde que se emitió");
    }

    [Fact]
    public async Task SinUsoPasadaLaVigencia_ElTokenVence()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var solicitud = await SembrarSolicitudAsync();
        var token = (await EnScopeAsync(s => s.IniciarAsync(solicitud, null, TestContext.Current.CancellationToken))).Token;

        _reloj.Avanzar(TimeSpan.FromMinutes(61));

        (await ValidarAsync(token)).Estado.Should().Be(EstadoSesion.Vencida);
    }

    [Fact]
    public async Task UnTokenRevocadoODesconocido_EsInvalido()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var solicitud = await SembrarSolicitudAsync();
        var token = (await EnScopeAsync(s => s.IniciarAsync(solicitud, null, TestContext.Current.CancellationToken))).Token;

        (await EnScopeAsync(s => s.RevocarDeSolicitudAsync(solicitud, TestContext.Current.CancellationToken))).Should().Be(1);

        (await ValidarAsync(token)).Estado.Should().Be(EstadoSesion.Invalida);
        (await ValidarAsync("token-que-nadie-emitio")).Estado.Should().Be(EstadoSesion.Invalida);
        (await ValidarAsync(null)).Estado.Should().Be(EstadoSesion.Invalida);
    }

    // ── Alcance: el token solo abre SU solicitud ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElTokenAbreSuSolicitud_YConOtraResponde403ConAuditoria()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var a = await SembrarSolicitudAsync("PN-A");
        var b = await SembrarSolicitudAsync("PN-B");
        var token = (await EnScopeAsync(s => s.IniciarAsync(a, null, TestContext.Current.CancellationToken))).Token;

        var propia = await GetAsync($"{SesionEndpoints.Base}/solicitudes/{a}", token);
        propia.StatusCode.Should().Be(HttpStatusCode.OK);

        var ajena = await GetAsync($"{SesionEndpoints.Base}/solicitudes/{b}", token);
        ajena.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodigoAsync(ajena)).Should().Be(SesionEndpoints.CodigoFueraDeAlcance);

        await using var db = NuevaDb();
        var auditoria = await db.Set<AccesoPiiLog>().SingleAsync(TestContext.Current.CancellationToken);
        auditoria.Accion.Should().Be(SesionEndpoints.AccionFueraDeAlcance);
        auditoria.SolicitudId.Should().Be(a, "se registra de quién era la sesión que intentó salirse");
    }

    [Fact]
    public async Task SinTokenOConTokenVencido_Responde401ConSuCodigo()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var a = await SembrarSolicitudAsync();
        var token = (await EnScopeAsync(s => s.IniciarAsync(a, null, TestContext.Current.CancellationToken))).Token;

        var sin = await GetAsync($"{SesionEndpoints.Base}/solicitudes/{a}", token: null);
        sin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodigoAsync(sin)).Should().Be(SesionEndpoints.CodigoSesionInvalida);

        _reloj.Avanzar(TimeSpan.FromMinutes(61));
        var vencida = await GetAsync($"{SesionEndpoints.Base}/solicitudes/{a}", token);
        vencida.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodigoAsync(vencida)).Should().Be(SesionEndpoints.CodigoSesionVencida);
    }

    // ── Reanudar con radicado y cédula ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1020456789")]
    [InlineData("1.020.456.789")]
    [InlineData(" 1020456789 ")]
    public async Task ConRadicadoYCedulaCorrectos_DevuelveTokenYEstado_AunqueLaCedulaVengaFormateada(string cedula)
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var id = await SembrarSolicitudAsync();

        var respuesta = await ReanudarAsync(Radicado.ToLowerInvariant(), cedula);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        cuerpo.GetProperty("solicitudId").GetGuid().Should().Be(id);
        cuerpo.GetProperty("estado").GetString().Should().Be("borrador");
        cuerpo.GetProperty("codigoPublico").GetString().Should().Be(Radicado);
        var token = cuerpo.GetProperty("token").GetString();
        (await GetAsync($"{SesionEndpoints.Base}/solicitudes/{id}", token)).StatusCode.Should().Be(HttpStatusCode.OK, "el token nuevo abre la solicitud");
        cuerpo.ToString().Should().NotContain(Cedula, "la cédula nunca se devuelve");
    }

    [Fact]
    public async Task RadicadoInexistenteOCedulaQueNoCoincide_RespondenExactamenteLoMismo()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await SembrarSolicitudAsync();

        var inexistente = await ReanudarAsync("PN-NO-EXISTE", Cedula);
        var cedulaMala = await ReanudarAsync(Radicado, "999999");

        inexistente.StatusCode.Should().Be(HttpStatusCode.NotFound);
        cedulaMala.StatusCode.Should().Be(inexistente.StatusCode);
        var cuerpoA = await inexistente.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var cuerpoB = await cedulaMala.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        cuerpoA.Should().Be(cuerpoB, "no se puede saber cuál de las dos cosas falló");
        cuerpoA.Should().Contain(SesionEndpoints.MensajeNoEncontrada).And.Contain(SesionEndpoints.CodigoNoEncontrada);
    }

    [Fact]
    public async Task UnaSolicitudCancelada_NoSeReanuda()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await SembrarSolicitudAsync(estado: EstadoSolicitud.Cancelada);

        (await ReanudarAsync(Radicado, Cedula)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CincoFallosSobreUnRadicado_LoBloquean429_AunConLosDatosCorrectos_YLiberaAlPasarLaVentana()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await SembrarSolicitudAsync();

        for (var i = 0; i < 5; i++)
            (await ReanudarAsync(Radicado, $"mal{i}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var sexto = await ReanudarAsync(Radicado, "mal5");
        sexto.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        sexto.Headers.RetryAfter.Should().NotBeNull();
        (await CodigoAsync(sexto)).Should().Be(SesionEndpoints.CodigoBloqueado);

        (await ReanudarAsync(Radicado, Cedula)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "mientras dure el bloqueo no se valida nada, ni los datos correctos");

        _reloj.Avanzar(TimeSpan.FromMinutes(16));
        (await ReanudarAsync(Radicado, Cedula)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ElBloqueoTambienCuentaParaRadicadosQueNoExisten_ParaNoDelatarCualesExisten()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);

        for (var i = 0; i < 5; i++)
            (await ReanudarAsync("PN-FANTASMA", $"c{i}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await ReanudarAsync("PN-FANTASMA", "c5")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData("", "123")]
    [InlineData("PN-1", "")]
    [InlineData("PN-1", "123456789012345678901234567890")]
    public async Task ConDatosVaciosOExcesivos_Responde400(string radicado, string cedula) =>
        await Reanudar400Async(radicado, cedula);

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task Reanudar400Async(string radicado, string cedula)
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        (await ReanudarAsync(radicado, cedula)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private Dictionary<string, string?> Settings() => new()
    {
        ["ConnectionStrings:Servicio"] = _database,
        ["Platform:ServiceClient:ClientSecret"] = "secreto-de-prueba",
        ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
        ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
        ["Platform:Auth:Issuers:0"] = "https://hub.prueba/",
        ["Platform:Messaging:ConnectionString"] = "amqp://flit:x@127.0.0.1:1/",
        [ServicioSettings.GrpcPortKey] = "5998",
        ["Pii:ClaveHmac"] = ClaveHmac,
        ["Captcha:SecretKey"] = "secreto-de-captcha",
    };

    private PersonaNaturalDb NuevaDb()
    {
        var options = new DbContextOptionsBuilder<PersonaNaturalDb>();
        PersonaNaturalDb.Configure(options, _database);
        return new PersonaNaturalDb(options.Options);
    }

    private async Task<T> EnScopeAsync<T>(Func<SesionService, Task<T>> accion)
    {
        using var scope = _app!.Services.CreateScope();
        return await accion(scope.ServiceProvider.GetRequiredService<SesionService>());
    }

    private Task<SesionValidada> ValidarAsync(string? token) => EnScopeAsync(s => s.ValidarAsync(token, TestContext.Current.CancellationToken));

    private async Task<Guid> SembrarSolicitudAsync(string radicado = Radicado, string estado = EstadoSolicitud.Borrador)
    {
        var hasher = new PiiHasher(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Pii:ClaveHmac"] = ClaveHmac }).Build());
        await using var db = NuevaDb();
        var solicitud = new SolicitudTramite
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.NewGuid(),
            CodigoPublico = radicado,
            TipoTramiteCodigo = "TRASPASO_STANDARD",
            DefinicionVersion = 1,
            OrganismoId = Guid.NewGuid(),
            OrganismoNombre = "Organismo de prueba",
            Placa = "ABC123",
            Estado = estado,
            CedulaSolicitanteHash = hasher.HashCedula(Cedula),
            CorrelationId = Guid.NewGuid(),
        };
        db.Add(solicitud);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return solicitud.Id;
    }

    private async Task<HttpResponseMessage> GetAsync(string ruta, string? token)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);
        if (token is not null)
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _http.SendAsync(peticion, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> ReanudarAsync(string radicado, string cedula)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, $"{SesionEndpoints.Base}/sesiones/reanudar") { Content = JsonContent.Create(new ReanudarRequest(radicado, cedula)) };
        peticion.Headers.Add(AbusoEndpoints.CabeceraCaptcha, CaptchaFalso.TokenValido);
        return await _http.SendAsync(peticion, TestContext.Current.CancellationToken);
    }

    private static async Task<string?> CodigoAsync(HttpResponseMessage respuesta)
    {
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return cuerpo.TryGetProperty("code", out var codigo) ? codigo.GetString() : null;
    }

    /// <summary>Reloj que solo avanza cuando la prueba lo manda.</summary>
    private sealed class RelojFalso(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _ahora = inicio;

        public override DateTimeOffset GetUtcNow() => _ahora;

        public void Avanzar(TimeSpan tiempo) => _ahora += tiempo;
    }
}
