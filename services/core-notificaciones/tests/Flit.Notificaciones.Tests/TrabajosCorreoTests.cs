using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Modules.Notificaciones;
using Flit.Modules.Security.Domain.Auth;
using Flit.Notificaciones.Api;
using Flit.Notificaciones.Api.Persistence;
using Flit.Notificaciones.Grpc.V1;
using Flit.Platform.Sdk.Authentication;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RabbitMQ.Client;
using Xunit;

namespace Flit.Notificaciones.Tests;

/// <summary>
/// HU #13354 (Epic #13316) — core-notificaciones atiende trabajos <c>notificaciones.email.send</c> desde el bus, contra
/// Postgres y RabbitMQ reales (<c>FLIT_TEST_RABBITMQ</c>; sin broker se omite): envía y registra (AC1); con el proveedor
/// caído reintenta y termina en <c>notificaciones.email.send.dlq</c>, con cada intento en el registro (AC2); un rechazo
/// definitivo no se reintenta. Esperas entre reintentos cortas para la prueba.
/// </summary>
[Collection(ColeccionBus.Nombre)]
public sealed class TrabajosCorreoTests : IAsyncLifetime
{
    private static readonly string RabbitMq = Environment.GetEnvironmentVariable("FLIT_TEST_RABBITMQ") ?? "amqp://flit:flit-prueba@127.0.0.1:5672/";
    private const string Cola = TrabajoCorreo.Tipo;

    private const int GrpcPort = 5995;
    private const string Issuer = "https://hub.prueba/";

    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "prueba" };
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly RentingFalso _renting = new();
    private string _database = string.Empty;
    private string? _skip;
    private WebApplication? _app;

    public async ValueTask InitializeAsync()
    {
        try
        {
            await using var probe = await new ConnectionFactory { Uri = new Uri(RabbitMq) }.CreateConnectionAsync();
        }
        catch (Exception ex) when (ex is RabbitMQ.Client.Exceptions.BrokerUnreachableException or System.Net.Sockets.SocketException)
        {
            _skip = $"RabbitMQ no alcanzable: {ex.Message}";
            return;
        }

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

        await LimpiarColasAsync();
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
                ["Platform:Messaging:ConnectionString"] = RabbitMq,
                ["Smtp:UseConsoleWhenNoHost"] = "true",
                ["Notificaciones:Correo:EsperasReintento:0"] = "00:00:00.200",
                ["Notificaciones:Correo:EsperasReintento:1"] = "00:00:00.300",
                ["Notificaciones:Correo:EsperasReintento:2"] = "00:00:00.400",
                [ServicioSettings.GrpcPortKey] = GrpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            b.Services.AddSingleton<IRentingEmailApiSender>(_renting);
            b.Services.AddHttpClient(PlatformAuthenticationExtensions.JwksHttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Jwks(_key));
        });
        await Program.MigrateAsync(_app);
        await _app.StartAsync();
        await EsperarAsync(async () => await ConsumidoresAsync() == 1);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
        if (_skip is null)
        {
            await LimpiarColasAsync();
            NpgsqlConnection.ClearAllPools();
            await using var db = new NotificacionesDb(new DbContextOptionsBuilder<NotificacionesDb>().UseNpgsql(_database).Options);
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task AC1_UnTrabajoDelBus_SeEnvia_YQuedaEnElRegistroConSuOrigen()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var trabajo = await PublicarAsync(Trabajo(CanalCorreo.FlitSmtp));

        await EsperarAsync(async () => await Entregas().AnyAsync(e => e.TrabajoId == trabajo.EventId));

        var entrega = await Entregas().SingleAsync(e => e.TrabajoId == trabajo.EventId, TestContext.Current.CancellationToken);
        entrega.Should().BeEquivalentTo(new
        {
            TenantId = (Guid?)_empresa, Plantilla = "tramites.aprobado", Canal = "flit_smtp", Resultado = "enviado", Origen = "tramites",
        });
    }

    [Fact]
    public async Task AC2_ConElProveedorCaido_SeReintenta_YTerminaEnLaDlq_ConCadaIntentoRegistrado()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        _renting.Respuesta = EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable);
        var trabajo = await PublicarAsync(Trabajo(CanalCorreo.EmpresaApi));

        await EsperarAsync(async () => await MensajesAsync($"{Cola}.dlq") == 1, TimeSpan.FromSeconds(20));

        var intentos = await Entregas().Where(e => e.TrabajoId == trabajo.EventId).ToListAsync(TestContext.Current.CancellationToken);
        intentos.Should().HaveCount(4, "el primero y uno por cada espera");
        intentos.Should().OnlyContain(e => e.Resultado == "fallido" && e.Desenlace == nameof(EmailSendOutcome.ProviderUnavailable));
        _renting.Pedidos.Should().HaveCount(4);
    }

    [Fact]
    public async Task UnRechazoDefinitivo_NoSeReintenta()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        _renting.Respuesta = EmailSendResult.Failed(EmailSendOutcome.RecipientRejected);
        var trabajo = await PublicarAsync(Trabajo(CanalCorreo.EmpresaApi));

        await EsperarAsync(async () => await Entregas().AnyAsync(e => e.TrabajoId == trabajo.EventId));
        await Task.Delay(1500, TestContext.Current.CancellationToken);

        (await Entregas().CountAsync(e => e.TrabajoId == trabajo.EventId, TestContext.Current.CancellationToken)).Should().Be(1);
        (await MensajesAsync($"{Cola}.dlq")).Should().Be(0);
    }

    [Fact]
    public async Task HU13357_UnCorreoMuerto_SeLista_YAlReintentarlo_VuelveASuColaYSale()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        _renting.Respuesta = EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable);
        var trabajo = await PublicarAsync(Trabajo(CanalCorreo.EmpresaApi));
        await EsperarAsync(async () => await MensajesAsync($"{Cola}.dlq") == 1, TimeSpan.FromSeconds(20));
        var admin = Admin(Token("platform.notificaciones.admin"));

        var lista = await admin.ListarMensajesMuertosAsync(new ListarMensajesMuertosRequest { Cola = ColaMuertos.Correos }, Empresa(Guid.NewGuid()), cancellationToken: ct);

        var muerto = lista.Mensajes.Should().ContainSingle().Subject;
        muerto.Id.Should().Be(trabajo.EventId.ToString());
        muerto.Tipo.Should().Be(TrabajoCorreo.Tipo);
        muerto.TenantId.Should().Be(_empresa.ToString());
        muerto.Productor.Should().Be("tramites");
        muerto.UltimoError.Should().Contain("ProviderUnavailable");
        muerto.MuertoEn.Should().NotBeNull();

        _renting.Respuesta = EmailSendResult.Sent;
        await admin.ReintentarMensajeMuertoAsync(new ReintentarMensajeMuertoRequest { Cola = ColaMuertos.Correos, Id = muerto.Id }, Empresa(Guid.NewGuid()), cancellationToken: ct);

        await EsperarAsync(async () => await Entregas().AnyAsync(e => e.TrabajoId == trabajo.EventId && e.Resultado == "enviado"));
        (await MensajesAsync($"{Cola}.dlq")).Should().Be(0);
        var otraVez = async () => await admin.DescartarMensajeMuertoAsync(
            new DescartarMensajeMuertoRequest { Cola = ColaMuertos.Correos, Id = muerto.Id }, Empresa(Guid.NewGuid()), cancellationToken: ct);
        (await otraVez.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public async Task HU13357_ConElScopeDeEnvio_NoSeAdministranLosMensajesMuertos()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);

        var llamada = async () => await Admin(Token("platform.notificaciones.send")).ListarMensajesMuertosAsync(
            new ListarMensajesMuertosRequest { Cola = ColaMuertos.Correos }, Empresa(Guid.NewGuid()), cancellationToken: TestContext.Current.CancellationToken);

        (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    private MensajesMuertosService.MensajesMuertosServiceClient Admin(string token)
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
        Audience = "notificaciones",
        Subject = new ClaimsIdentity([new Claim("sub", "svc-tramites"), new Claim("scope", scope)]),
        Expires = DateTime.UtcNow.AddMinutes(10),
        SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
    });

    private sealed class Jwks(RsaSecurityKey key) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(key.Rsa.ExportParameters(false)) { KeyId = key.KeyId });
            jwk.Use = "sig";
            jwk.Alg = SecurityAlgorithms.RsaSha256;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { keys = new[] { jwk } })) });
        }
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static TrabajoCorreo Trabajo(CanalCorreo canal) => TrabajoCorreo.De(
        new EmailMessage(null, "tramites.aprobado", "cliente@prueba.test", "Cliente", "Su trámite fue aprobado", "<p>Hola</p>"), canal);

    /// <summary>Publica como lo hace Trámites: un trabajo del SDK, con «tramites» de productor, en flit.notificaciones.</summary>
    private async Task<EventEnvelope> PublicarAsync(TrabajoCorreo trabajo)
    {
        var sobre = new EventEnvelope(Guid.CreateVersion7(), TrabajoCorreo.Tipo, 1, DateTimeOffset.UtcNow, _empresa, "tramites", "corr",
            System.Text.Json.JsonSerializer.SerializeToElement(trabajo, EventEnvelope.JsonOptions));
        await using var publicador = new RabbitMqEventPublisher(new PlatformMessagingOptions { Producer = "tramites", ConnectionString = RabbitMq });
        await publicador.PublishAsync(new OutboxMessage
        {
            Id = sobre.EventId, Exchange = "flit.notificaciones", RoutingKey = sobre.Type, Payload = sobre.ToJson(), OccurredAt = sobre.OccurredAt,
        }, TestContext.Current.CancellationToken);
        return sobre;
    }

    private IQueryable<Api.Persistence.Entrega> Entregas() =>
        _app!.Services.CreateScope().ServiceProvider.GetRequiredService<NotificacionesDb>().Entregas.AsNoTracking();

    private static async Task<uint> ConsumidoresAsync()
    {
        await using var conexion = await new ConnectionFactory { Uri = new Uri(RabbitMq) }.CreateConnectionAsync();
        await using var canal = await conexion.CreateChannelAsync();
        try { return await canal.ConsumerCountAsync(Cola); }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException) { return 0; }
    }

    private static async Task<uint> MensajesAsync(string cola)
    {
        await using var conexion = await new ConnectionFactory { Uri = new Uri(RabbitMq) }.CreateConnectionAsync();
        await using var canal = await conexion.CreateChannelAsync();
        try { return await canal.MessageCountAsync(cola); }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException) { return 0; }
    }

    private static async Task LimpiarColasAsync()
    {
        await using var conexion = await new ConnectionFactory { Uri = new Uri(RabbitMq) }.CreateConnectionAsync();
        await using var canal = await conexion.CreateChannelAsync();
        foreach (var c in new[] { Cola, $"{Cola}.retry.1", $"{Cola}.retry.2", $"{Cola}.retry.3", $"{Cola}.dlq" })
            await canal.QueueDeleteAsync(c);
        await canal.ExchangeDeleteAsync($"{Cola}.reintentos");
    }

    private static async Task EsperarAsync(Func<Task<bool>> condicion, TimeSpan? limite = null)
    {
        var reloj = Stopwatch.StartNew();
        while (!await condicion())
        {
            if (reloj.Elapsed > (limite ?? TimeSpan.FromSeconds(10)))
                throw new TimeoutException("La condición no se cumplió a tiempo.");
            await Task.Delay(100);
        }
    }

    /// <summary>API de correo de Renting falsa: responde lo que se le indique y guarda cada pedido.</summary>
    private sealed class RentingFalso : IRentingEmailApiSender
    {
        public EmailSendResult Respuesta { get; set; } = EmailSendResult.Sent;

        public List<RentingSendEmailRequest> Pedidos { get; } = [];

        public Task<EmailSendResult> SendAsync(RentingSendEmailRequest request, CancellationToken cancellationToken)
        {
            lock (Pedidos)
                Pedidos.Add(request);
            return Task.FromResult(Respuesta);
        }

        public Task<EmailSendResult> SendAsync(RentingSendEmailRequest request, ControlledMailboxRecipient recipient, CancellationToken cancellationToken) =>
            SendAsync(request, cancellationToken);
    }
}
