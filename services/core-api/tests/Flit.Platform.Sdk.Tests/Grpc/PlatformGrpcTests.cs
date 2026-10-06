using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Flit.Platform.Sdk.Authentication;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Tests.Prueba;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Health.V1;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Platform.Sdk.Tests.Grpc;

/// <summary>
/// HU #13337 (Epic #13316) — cliente y servidor gRPC del SDK contra un servidor real en memoria: metadata y deadline
/// (AC1), token de usuario (AC2), circuito (AC3) y errores del §10 (AC4).
/// </summary>
public sealed class PlatformGrpcTests : IAsyncLifetime
{
    private const string Scope = "platform.consultas";
    private const string Audience = "consultas";

    private readonly TestKeys _keys = new();
    private readonly JwksHandler _jwks = new();
    private readonly PruebaImpl.Contador _contador = new();
    private WebApplication _server = null!;

    public async ValueTask InitializeAsync()
    {
        _jwks.Keys = [_keys.Key];
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Platform:Auth:Audience"] = Audience,
            ["Platform:Auth:Issuers:0"] = TestKeys.Issuer,
            ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
        });
        builder.Services.AddFlitPlatformAuthentication(builder.Configuration);
        builder.Services.AddHttpClient(PlatformAuthenticationExtensions.JwksHttpClientName).ConfigurePrimaryHttpMessageHandler(() => _jwks);
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(_contador);
        builder.Services.AddFlitGrpcServer().RequireServiceToken<PruebaImpl>(Scope, Audience);

        _server = builder.Build();
        _server.UseAuthentication();
        _server.UseAuthorization();
        _server.MapGrpcService<PruebaImpl>();
        _server.MapFlitGrpcPlatform(_server.Environment);
        await _server.StartAsync();
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    // ── AC1: metadata y deadline ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElCliente_EnviaTokenEmpresaCorrelacionYTraceparent_ConDeadlineDe3s()
    {
        var tenant = Guid.NewGuid();
        using var client = Client(http: Peticion(tenant, "corr-123"));
        using var activity = new Activity("prueba-sdk").Start();

        var eco = await client.Get().EcoAsync(new EcoRequest { Modo = "ok" }, cancellationToken: TestContext.Current.CancellationToken);

        eco.Authorization.Should().StartWith("Bearer ");
        eco.Cliente.Should().Be("svc-tramites");
        eco.Tenant.Should().Be(tenant.ToString());
        eco.Correlation.Should().Be("corr-123");
        eco.Traceparent.Should().Contain(activity.TraceId.ToString());
        eco.DeadlineMs.Should().BeInRange(2_000, 3_000);
    }

    [Fact]
    public async Task SinPeticionEnCurso_CreaCorrelacion_YLaEmpresaExplicitaGana()
    {
        var tenant = Guid.NewGuid();
        using var client = Client();

        var eco = await client.Get().EcoAsync(new EcoRequest { Modo = "ok" },
            new Metadata { { "x-flit-tenant-id", tenant.ToString() } }, cancellationToken: TestContext.Current.CancellationToken);

        eco.Tenant.Should().Be(tenant.ToString());
        Guid.TryParse(eco.Correlation, out _).Should().BeTrue();
    }

    [Fact]
    public async Task ElTokenSePideUnaVezPorScope()
    {
        using var client = Client(http: Peticion(Guid.NewGuid()));
        var ct = TestContext.Current.CancellationToken;

        await client.Get().EcoAsync(new EcoRequest { Modo = "ok" }, cancellationToken: ct);
        await client.Get().EcoAsync(new EcoRequest { Modo = "ok" }, cancellationToken: ct);

        client.TokenRequests.Should().Be(1);
    }

    [Fact]
    public async Task SinEmpresa_ElServidorRespondeInvalidArgument()
    {
        using var client = Client();
        var call = async () => await client.Get().EcoAsync(new EcoRequest { Modo = "ok" }, cancellationToken: TestContext.Current.CancellationToken);
        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    // ── AC2: token de usuario ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TokenDeUsuario_Unauthenticated()
    {
        var userToken = _keys.Token(Guid.NewGuid().ToString(), Audience, Scope);
        var call = async () => await Raw(userToken).EcoAsync(new EcoRequest { Modo = "ok" },
            new Metadata { { "x-flit-tenant-id", Guid.NewGuid().ToString() } }, cancellationToken: TestContext.Current.CancellationToken);
        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task TokenDeServicioParaOtroDestino_Unauthenticated_PorAudiencia()
    {
        // aud=tramites: la autenticación del destino (aud=consultas) ni siquiera lo acepta.
        var token = _keys.Token("svc-tramites", "tramites", "platform.tramites.ict");
        var call = async () => await Raw(token).EcoAsync(new EcoRequest { Modo = "ok" },
            new Metadata { { "x-flit-tenant-id", Guid.NewGuid().ToString() } }, cancellationToken: TestContext.Current.CancellationToken);
        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task TokenDeServicioSinElScope_PermissionDenied()
    {
        var token = _keys.Token("svc-notificaciones", Audience, "platform.manifest");
        var call = async () => await Raw(token).EcoAsync(new EcoRequest { Modo = "ok" },
            new Metadata { { "x-flit-tenant-id", Guid.NewGuid().ToString() } }, cancellationToken: TestContext.Current.CancellationToken);
        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    // ── AC3: destino caído ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaMitadFallaEn30s_ElCircuitoSeAbre15s_YFallaDeInmediatoConUnavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        using var client = Client(http: Peticion(Guid.NewGuid()), clock: clock, configure: o => o.MaxRetries = 0);

        for (var i = 0; i < 10; i++)
        {
            var modo = i % 2 == 0 ? "ok" : "unavailable";
            try
            {
                await client.Get().EcoAsync(new EcoRequest { Modo = modo }, cancellationToken: ct);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
            {
            }

            clock.Advance(TimeSpan.FromSeconds(2));
        }

        var llegadas = _contador.Llamadas;
        var abierto = async () => await client.Get().EcoAsync(new EcoRequest { Modo = "ok" }, cancellationToken: ct);
        (await abierto.Should().ThrowAsync<RpcException>()).Which.Should().Match<RpcException>(e =>
            e.StatusCode == StatusCode.Unavailable && e.Status.Detail.Contains("Circuito abierto"));
        _contador.Llamadas.Should().Be(llegadas, "con el circuito abierto la llamada no sale");

        clock.Advance(TimeSpan.FromSeconds(16));
        (await client.Get().EcoAsync(new EcoRequest { Modo = "ok" }, cancellationToken: ct)).Cliente.Should().Be("svc-tramites");
    }

    [Fact]
    public async Task Unavailable_SeReintenta_YOtrosErroresNo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = Client(http: Peticion(Guid.NewGuid()), configure: o => o.RetryDelay = TimeSpan.Zero);

        _contador.FallarLasPrimeras(1);
        (await client.Get().EcoAsync(new EcoRequest { Modo = "ok" }, cancellationToken: ct)).Cliente.Should().Be("svc-tramites");
        _contador.Llamadas.Should().Be(2);

        var antes = _contador.Llamadas;
        var negocio = async () => await client.Get().EcoAsync(new EcoRequest { Modo = "producto_apagado" }, cancellationToken: ct);
        await negocio.Should().ThrowAsync<RpcException>();
        _contador.Llamadas.Should().Be(antes + 1);
    }

    // ── AC4: errores ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlatformException_PermissionDenied_ConErrorInfo()
    {
        using var client = Client(http: Peticion(Guid.NewGuid()));
        var call = async () => await client.Get().EcoAsync(new EcoRequest { Modo = "producto_apagado" }, cancellationToken: TestContext.Current.CancellationToken);

        var error = (await call.Should().ThrowAsync<RpcException>()).Which;
        error.StatusCode.Should().Be(StatusCode.PermissionDenied);
        PlatformRpcErrors.Reason(error).Should().Be(PlatformErrorCodes.ProductNotEnabled);
    }

    // ── Salud y reflection ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExponeGrpcHealth_YNoReflectionFueraDeDesarrollo()
    {
        var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = _server.GetTestServer().CreateHandler() });
        (await new Health.HealthClient(channel).CheckAsync(new HealthCheckRequest(), cancellationToken: TestContext.Current.CancellationToken))
            .Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);

        _server.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints)
            .Should().NotContain(e => (e.DisplayName ?? string.Empty).Contains("ServerReflection", StringComparison.Ordinal));
    }

    [Fact]
    public void SinClienteDeServicioConfigurado_NoArranca()
    {
        var act = () => new ServiceCollection().AddFlitGrpcClient<PruebaService.PruebaServiceClient>(
            new ConfigurationBuilder().Build(), new Uri("http://localhost"), Scope);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Platform:ServiceClient*");
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────────────────────────

    private PruebaService.PruebaServiceClient Raw(string token)
    {
        var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = _server.GetTestServer().CreateHandler() });
        return new PruebaService.PruebaServiceClient(channel.CreateCallInvoker().Intercept(m =>
        {
            m.Add("authorization", $"Bearer {token}");
            return m;
        }));
    }

    private static DefaultHttpContext Peticion(Guid tenant, string? correlation = null)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString()), new Claim("tenant_id", tenant.ToString())], "Bearer")),
        };
        if (correlation is not null)
            http.Request.Headers["X-Correlation-Id"] = correlation;
        return http;
    }

    private ClienteDePrueba Client(HttpContext? http = null, ManualClock? clock = null, Action<PlatformGrpcClientOptions>? configure = null)
    {
        var tokenHandler = new TokenEndpointHandler(_keys);
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
            ["Platform:ServiceClient:ClientId"] = "svc-tramites",
            ["Platform:ServiceClient:ClientSecret"] = "secreto",
        }).Build();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = http });
        if (clock is not null)
            services.AddSingleton<TimeProvider>(clock);
        services.AddFlitGrpcClient<PruebaService.PruebaServiceClient>(config, new Uri("http://localhost"), Scope, configure)
            .ConfigurePrimaryHttpMessageHandler(() => _server.GetTestServer().CreateHandler());
        services.AddHttpClient(ServiceTokenProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => tokenHandler);
        return new ClienteDePrueba(services.BuildServiceProvider(), tokenHandler);
    }

    private sealed class ClienteDePrueba(ServiceProvider provider, TokenEndpointHandler tokens) : IDisposable
    {
        public int TokenRequests => tokens.Requests;

        public PruebaService.PruebaServiceClient Get() => provider.GetRequiredService<PruebaService.PruebaServiceClient>();

        public void Dispose() => provider.Dispose();
    }

    /// <summary>Endpoint de token falso: emite un token de servicio de svc-tramites firmado con la llave de prueba.</summary>
    private sealed class TokenEndpointHandler(TestKeys keys) : HttpMessageHandler
    {
        private int _requests;

        public int Requests => _requests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            var scope = Uri.UnescapeDataString(form.Split('&').Single(p => p.StartsWith("scope=", StringComparison.Ordinal))["scope=".Length..]);
            var body = JsonSerializer.Serialize(new { access_token = keys.Token("svc-tramites", Audience, scope), expires_in = 900 });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private long _ticks = now.UtcTicks;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

        public override long GetTimestamp() => Interlocked.Read(ref _ticks);

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }
}

/// <summary>Implementación del servicio de prueba: devuelve lo que recibió.</summary>
internal sealed class PruebaImpl(PruebaImpl.Contador contador) : PruebaService.PruebaServiceBase
{
    public override Task<EcoResponse> Eco(EcoRequest request, ServerCallContext context)
    {
        contador.Llegó();
        if (contador.DebeFallar() || request.Modo == "unavailable")
            throw new RpcException(new Status(StatusCode.Unavailable, "caído a propósito"));
        if (request.Modo == "producto_apagado")
            throw new PlatformException(PlatformErrorCodes.ProductNotEnabled, "Consultas está apagado para la empresa.");

        var caller = PlatformServiceCaller.From(context);
        return Task.FromResult(new EcoResponse
        {
            Authorization = context.RequestHeaders.GetValue("authorization") ?? string.Empty,
            Tenant = caller.TenantId.ToString(),
            Correlation = context.RequestHeaders.GetValue("x-correlation-id") ?? string.Empty,
            Traceparent = context.RequestHeaders.GetValue("traceparent") ?? string.Empty,
            DeadlineMs = context.Deadline == DateTime.MaxValue ? 0 : (long)(context.Deadline - DateTime.UtcNow).TotalMilliseconds,
            Cliente = caller.ClientId,
        });
    }

    internal sealed class Contador
    {
        private int _llamadas;
        private int _fallarPrimeras;

        public int Llamadas => Volatile.Read(ref _llamadas);

        public void Llegó() => Interlocked.Increment(ref _llamadas);

        public void FallarLasPrimeras(int n) => Volatile.Write(ref _fallarPrimeras, n);

        public bool DebeFallar() => Interlocked.Decrement(ref _fallarPrimeras) >= 0;
    }
}
