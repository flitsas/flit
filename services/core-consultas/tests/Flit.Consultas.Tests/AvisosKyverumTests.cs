using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Consultas.Api;
using Flit.Consultas.Api.Avisos;
using Flit.Consultas.Api.Persistence;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Authentication;
using Flit.Platform.Sdk.Messaging;
using Flit.Tramites.Application.Identity;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace Flit.Consultas.Tests;

/// <summary>
/// HU #13351 (Epic #13316, ADR-0065 §6-7) — Kyverum Verify por Consultas contra core-consultas real (Postgres efímero):
/// Consultas crea la validación, guarda el secreto cifrado y no lo devuelve; el aviso con firma válida se guarda tal
/// como llegó y queda en la outbox para el bus (AC1); con firma inválida se rechaza, se registra y no se publica (AC2).
/// Kyverum es un HTTP falso; la publicación al broker la cubren las pruebas del SDK.
/// </summary>
public sealed class AvisosKyverumTests : IAsyncLifetime
{
    private const int GrpcPort = 5997;
    private const string Issuer = "https://hub.prueba/";
    private const string SecretoKyverum = "whsec_de_prueba";

    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "prueba" };
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly KyverumFalso _kyverum = new();
    private string _database = string.Empty;
    private string? _skip;
    private WebApplication? _app;

    public async ValueTask InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_svc_{Guid.NewGuid():N}"[..30] }.ConnectionString;
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
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Servicio"] = _database,
                ["Platform:ServiceClient:ClientSecret"] = "secreto",
                ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
                ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
                ["Platform:Auth:Issuers:0"] = Issuer,
                ["Platform:Messaging:ConnectionString"] = "amqp://flit:x@127.0.0.1:1/",
                ["Consultations:VerifikVehicleMode"] = "mock",
                ["ImprontaRunt:ApiKey"] = "llave-de-prueba",
                ["Kyverum:BaseUrl"] = "https://verify.kyverum.prueba",
                ["Kyverum:ApiKey"] = "llave-kyverum",
                ["Kyverum:WebhookCallbackUrl"] = "https://dev.flit.prueba/api/v1/consultas/avisos/kyverum-verify",
                [ServicioSettings.GrpcPortKey] = GrpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            b.Services.AddHttpClient(PlatformAuthenticationExtensions.JwksHttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Jwks(_key));
            // Nombre del cliente tipado de AddHttpClient<IKyverumVerifyClient, …>: Kyverum no sale a la red.
            b.Services.AddHttpClient(nameof(IKyverumVerifyClient)).ConfigurePrimaryHttpMessageHandler(() => _kyverum);
        });
        await Program.MigrateAsync(_app);
        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
        if (_skip is null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var db = new ConsultasDb(new DbContextOptionsBuilder<ConsultasDb>().UseNpgsql(_database).Options);
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Iniciar_CreaEnKyverumConLaUrlDeConsultas_YGuardaElSecretoCifradoSinDevolverlo()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var validacion = Guid.NewGuid();

        var respuesta = await Cliente(Token("platform.consultas")).IniciarValidacionAsync(Pedido(validacion), Empresa(_empresa), cancellationToken: ct);

        respuesta.VerificationId.Should().Be("kyv_123");
        respuesta.CaptureUrl.Should().StartWith("https://verify.kyverum.prueba/session/");
        _kyverum.UltimoCuerpo.Should().Contain($"/api/v1/consultas/avisos/kyverum-verify/{validacion:D}");
        await using var scope = _app!.Services.CreateAsyncScope();
        var fila = await scope.ServiceProvider.GetRequiredService<ConsultasDb>().ValidacionesKyverum.SingleAsync(v => v.ValidacionId == validacion, ct);
        fila.TenantId.Should().Be(_empresa);
        fila.Producto.Should().Be("tramites");
        fila.SecretoCifrado.Should().NotBeNullOrEmpty().And.NotContain(SecretoKyverum);
    }

    [Fact]
    public async Task Iniciar_ConKyverumCaido_Unavailable()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        _kyverum.Estado = HttpStatusCode.ServiceUnavailable;

        var llamada = async () => await Cliente(Token("platform.consultas")).IniciarValidacionAsync(
            Pedido(Guid.NewGuid()), Empresa(_empresa), cancellationToken: TestContext.Current.CancellationToken);

        (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unavailable);
    }

    [Fact]
    public async Task AC1_AvisoConFirmaValida_SeGuardaTalComoLlego_Responde200_YQuedaParaElBus()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var validacion = Guid.NewGuid();
        await Cliente(Token("platform.consultas")).IniciarValidacionAsync(Pedido(validacion), Empresa(_empresa), cancellationToken: ct);
        const string cuerpo = """{"evento":"validation.approved","ts":"2026-10-06T12:00:00Z","data":{"aprobado":true}}""";

        var respuesta = await AvisarAsync(validacion, cuerpo, Firma(cuerpo, SecretoKyverum), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
        var aviso = await db.Avisos.SingleAsync(a => a.ReferenciaId == validacion, ct);
        aviso.Resultado.Should().Be(AvisoResultados.Publicado);
        aviso.Cuerpo.Should().Be(cuerpo);
        aviso.TenantId.Should().Be(_empresa);

        var mensaje = await db.Set<OutboxMessage>().SingleAsync(m => m.RoutingKey == AvisosKyverumEndpoints.TipoAviso && m.Payload.Contains(validacion.ToString()), ct);
        mensaje.Exchange.Should().Be("flit.consultas");
        var sobre = EventEnvelope.FromJson(Encoding.UTF8.GetBytes(mensaje.Payload));
        sobre.TenantId.Should().Be(_empresa);
        var datos = sobre.DataAs<AvisoKyverumVerify>();
        datos.ValidacionId.Should().Be(validacion);
        datos.VerificationId.Should().Be("kyv_123");
        datos.Producto.Should().Be("tramites");
        datos.Cuerpo.Should().Be(cuerpo);
    }

    [Fact]
    public async Task AC2_AvisoConFirmaInvalida_SeRechaza_SeRegistra_YNoSePublica()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var validacion = Guid.NewGuid();
        await Cliente(Token("platform.consultas")).IniciarValidacionAsync(Pedido(validacion), Empresa(_empresa), cancellationToken: ct);
        const string cuerpo = """{"evento":"validation.approved","data":{"aprobado":true}}""";

        var respuesta = await AvisarAsync(validacion, cuerpo, Firma(cuerpo, "otro-secreto"), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
        (await db.Avisos.SingleAsync(a => a.ReferenciaId == validacion, ct)).Resultado.Should().Be(AvisoResultados.FirmaInvalida);
        (await db.Set<OutboxMessage>().AnyAsync(m => m.Payload.Contains(validacion.ToString()), ct)).Should().BeFalse();
    }

    [Fact]
    public async Task UnAvisoDeUnaValidacionDesconocida_404_YNoSePublica()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var validacion = Guid.NewGuid();
        const string cuerpo = """{"evento":"validation.approved"}""";

        var respuesta = await AvisarAsync(validacion, cuerpo, Firma(cuerpo, SecretoKyverum), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var scope = _app!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
        (await db.Avisos.SingleAsync(a => a.ReferenciaId == validacion, ct)).Resultado.Should().Be(AvisoResultados.ReferenciaDesconocida);
        (await db.Set<OutboxMessage>().AnyAsync(m => m.Payload.Contains(validacion.ToString()), ct)).Should().BeFalse();
    }

    [Fact]
    public async Task ElServicioDeValidacion_ExigeElScopeDeConsultas()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);

        var llamada = async () => await Cliente(Token("platform.identidad.read")).IniciarValidacionAsync(
            Pedido(Guid.NewGuid()), Empresa(_empresa), cancellationToken: TestContext.Current.CancellationToken);

        (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> AvisarAsync(Guid validacion, string cuerpo, string firma, CancellationToken ct)
    {
        using var http = _app!.GetTestClient();
        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/consultas/avisos/kyverum-verify/{validacion:D}")
        {
            Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
        };
        pedido.Headers.Add(KyverumWebhookVerifier.SignatureHeader, firma);
        return await http.SendAsync(pedido, ct);
    }

    private static string Firma(string cuerpo, string secreto) => KyverumWebhookVerifier.ComputeHmac(Encoding.UTF8.GetBytes(cuerpo), secreto);

    private static IniciarValidacionRequest Pedido(Guid validacion) => new()
    {
        ValidacionId = validacion.ToString(),
        TramiteId = Guid.NewGuid().ToString(),
        Parte = "comprador",
        Nombre = "Ana Prueba",
        TipoDocumento = "CC",
        Documento = "1000000001",
        Email = "ana@prueba.test",
    };

    private ValidacionIdentidadService.ValidacionIdentidadServiceClient Cliente(string token)
    {
        var channel = GrpcChannel.ForAddress($"http://localhost:{GrpcPort}", new GrpcChannelOptions { HttpHandler = _app!.GetTestServer().CreateHandler() });
        return new(channel.CreateCallInvoker().Intercept(m =>
        {
            m.Add("authorization", $"Bearer {token}");
            return m;
        }));
    }

    private static Metadata Empresa(Guid tenant) => new() { { "x-flit-tenant-id", tenant.ToString() } };

    private string Token(string scope) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = "consultas",
        Subject = new ClaimsIdentity([new Claim("sub", "svc-tramites"), new Claim("scope", scope)]),
        Expires = DateTime.UtcNow.AddMinutes(10),
        SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
    });

    /// <summary>Kyverum Verify falso: crea validaciones con un secreto fijo, o responde el estado configurado.</summary>
    private sealed class KyverumFalso : HttpMessageHandler
    {
        public HttpStatusCode Estado { get; set; } = HttpStatusCode.Created;

        public string UltimoCuerpo { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UltimoCuerpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var cuerpo = Estado == HttpStatusCode.Created
                ? $$"""{"id":"kyv_123","status":"pending","webhookSecret":"{{SecretoKyverum}}","captureLinks":[{"captureUrl":"https://verify.kyverum.prueba/session/s1?t=abc"}]}"""
                : """{"error":"caido"}""";
            return new HttpResponseMessage(Estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Jwks(RsaSecurityKey key) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(key.Rsa.ExportParameters(false)) { KeyId = key.KeyId });
            jwk.Use = "sig";
            jwk.Alg = SecurityAlgorithms.RsaSha256;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { keys = new[] { jwk } })) });
        }
    }
}
