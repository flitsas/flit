using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Modules.Security.Domain.Auth;
using Flit.Notificaciones.Api;
using Flit.Notificaciones.Api.Persistence;
using Flit.Notificaciones.Grpc.V1;
using Flit.Platform.Sdk.Authentication;
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

namespace Flit.Notificaciones.Tests;

/// <summary>
/// HU #13353 (Epic #13316) — core-notificaciones envía un correo ya armado por el canal que pidió quien lo armó y deja
/// cada intento en <c>notificaciones.entregas</c>. Postgres real; el SMTP es la consola y la API de Renting un adaptador
/// falso (sin red ni certificado).
/// </summary>
public sealed class EnvioDeCorreoTests : IAsyncLifetime
{
    private const int GrpcPort = 5996;
    private const string Issuer = "https://hub.prueba/";

    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "prueba" };
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly RentingFalso _renting = new();
    private string _database = string.Empty;
    private string? _skip;
    private WebApplication? _app;

    public ValueTask InitializeAsync() => IniciarAsync(conRenting: true);

    private async ValueTask IniciarAsync(bool conRenting)
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
                ["Smtp:UseConsoleWhenNoHost"] = "true",
                [ServicioSettings.GrpcPortKey] = GrpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            b.Services.AddHttpClient(PlatformAuthenticationExtensions.JwksHttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Jwks(_key));
            if (conRenting)
                b.Services.AddSingleton<IRentingEmailApiSender>(_renting);
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
            await using var db = new NotificacionesDb(new DbContextOptionsBuilder<NotificacionesDb>().UseNpgsql(_database).Options);
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task AC1_ConElCanalDeLaEmpresa_SaleConLaPlantillaYElTemaDeQuienLoArmo_YQuedaRegistrado()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;

        var r = await Cliente(Token()).EnviarCorreoAsync(new EnviarCorreoRequest { Correo = Correo(Canal.EmpresaApi) }, Empresa(_empresa), cancellationToken: ct);

        r.Resultado.Should().Be(ResultadoEnvio.Enviado);
        var enviado = _renting.Pedidos.Should().ContainSingle().Subject;
        enviado.Subject.Should().Be("Su trámite fue aprobado");
        enviado.Body.Should().Be("<p>Marca de la empresa</p>", "el correo sale tal como lo armó quien lo pidió");
        enviado.Recipients.Should().ContainSingle(t => t.Email == "cliente@prueba.test");

        var entrega = await Entregas().SingleAsync(e => e.Id == Guid.Parse(r.EntregaId), ct);
        entrega.Should().BeEquivalentTo(new
        {
            TenantId = (Guid?)_empresa, Plantilla = "tramites.aprobado", Canal = "tenant_api", Destinatario = "cliente@prueba.test",
            Resultado = "enviado", Tema = "brand", TemaVersion = (int?)3, Origen = "tramites",
        });
    }

    [Fact]
    public async Task ElCanalDeFlit_SalePorElTransporteDeFlit()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;

        var r = await Cliente(Token()).EnviarCorreoAsync(new EnviarCorreoRequest { Correo = Correo(Canal.FlitSmtp) }, Empresa(_empresa), cancellationToken: ct);

        r.Resultado.Should().Be(ResultadoEnvio.Enviado);
        _renting.Pedidos.Should().BeEmpty();
        (await Entregas().SingleAsync(e => e.Id == Guid.Parse(r.EntregaId), ct)).Canal.Should().Be("flit_smtp");
    }

    [Fact]
    public async Task AC2_ConElCanalDeLaEmpresaSinConfigurar_FallaYQuedaEnElRegistro()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        await DisposeAsync();
        _skip = null;
        await IniciarAsync(conRenting: false);
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;

        var r = await Cliente(Token()).EnviarCorreoAsync(new EnviarCorreoRequest { Correo = Correo(Canal.EmpresaApi) }, Empresa(_empresa), cancellationToken: ct);

        r.Resultado.Should().Be(ResultadoEnvio.ConfiguracionIncompleta, "nunca cae a SMTP en silencio");
        var entrega = await Entregas().SingleAsync(e => e.Id == Guid.Parse(r.EntregaId), ct);
        entrega.Resultado.Should().Be("fallido");
        entrega.Desenlace.Should().Be(nameof(EmailSendOutcome.ConfigurationIncomplete));
        entrega.MotivoFallo.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ListarEntregas_SoloTraeLasDeLaEmpresaDeLaLlamada()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var cliente = Cliente(Token());
        await cliente.EnviarCorreoAsync(new EnviarCorreoRequest { Correo = Correo(Canal.FlitSmtp) }, Empresa(_empresa), cancellationToken: ct);
        await cliente.EnviarCorreoAsync(new EnviarCorreoRequest { Correo = Correo(Canal.FlitSmtp) }, Empresa(Guid.NewGuid()), cancellationToken: ct);

        var lista = await cliente.ListarEntregasAsync(new ListarEntregasRequest(), Empresa(_empresa), cancellationToken: ct);

        lista.Total.Should().Be(1);
        lista.Entregas.Should().ContainSingle().Which.Plantilla.Should().Be("tramites.aprobado");
    }

    [Fact]
    public async Task SinElScopeDeEnvio_PermissionDenied()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);

        var llamada = async () => await Cliente(Token("platform.consultas")).EnviarCorreoAsync(
            new EnviarCorreoRequest { Correo = Correo(Canal.FlitSmtp) }, Empresa(_empresa), cancellationToken: TestContext.Current.CancellationToken);

        (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────────────────────────

    private IQueryable<Api.Persistence.Entrega> Entregas()
    {
        var scope = _app!.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<NotificacionesDb>().Entregas.AsNoTracking();
    }

    private static Correo Correo(Canal canal) => new()
    {
        Plantilla = "tramites.aprobado",
        Canal = canal,
        DestinatarioEmail = "cliente@prueba.test",
        DestinatarioNombre = "Cliente",
        Asunto = "Su trámite fue aprobado",
        Html = "<p>Marca de la empresa</p>",
        Tema = "brand",
        TemaVersion = 3,
    };

    private NotificacionesService.NotificacionesServiceClient Cliente(string token)
    {
        var channel = GrpcChannel.ForAddress($"http://localhost:{GrpcPort}", new GrpcChannelOptions { HttpHandler = _app!.GetTestServer().CreateHandler() });
        return new(channel.CreateCallInvoker().Intercept(m =>
        {
            m.Add("authorization", $"Bearer {token}");
            return m;
        }));
    }

    private static Metadata Empresa(Guid tenant) => new() { { "x-flit-tenant-id", tenant.ToString() } };

    private string Token(string scope = "platform.notificaciones.send") => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = "notificaciones",
        Subject = new ClaimsIdentity([new Claim("sub", "svc-tramites"), new Claim("scope", scope)]),
        Expires = DateTime.UtcNow.AddMinutes(10),
        SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
    });

    /// <summary>API de correo de Renting falsa: guarda lo que le piden y responde enviado.</summary>
    private sealed class RentingFalso : IRentingEmailApiSender
    {
        public List<RentingSendEmailRequest> Pedidos { get; } = [];

        public Task<EmailSendResult> SendAsync(RentingSendEmailRequest request, CancellationToken cancellationToken)
        {
            Pedidos.Add(request);
            return Task.FromResult(EmailSendResult.Sent);
        }

        public Task<EmailSendResult> SendAsync(RentingSendEmailRequest request, ControlledMailboxRecipient recipient, CancellationToken cancellationToken) =>
            SendAsync(request, cancellationToken);
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
