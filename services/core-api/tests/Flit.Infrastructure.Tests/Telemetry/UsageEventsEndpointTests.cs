using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Flit.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Infrastructure.Tests.Telemetry;

/// <summary>
/// Reportes2 HU-A — endpoint batch <c>POST /api/v1/analytics/events</c> (contrato §4.6):
/// auth requerida, 202 con el conteo de aceptados, máximo 50 eventos (más → 400 en español),
/// descarte silencioso de <c>eventType</c> fuera de taxonomía y tenant/userId SIEMPRE del JWT
/// (el body no puede suplantarlos).
/// </summary>
public sealed class UsageEventsEndpointTests : IClassFixture<UsageEventsEndpointTests.TelemetryWebApplicationFactory>
{
    private const string EventsUrl = "/api/v1/analytics/events";
    private static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly TelemetryWebApplicationFactory _factory;

    public UsageEventsEndpointTests(TelemetryWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Queue.Clear();
    }

    [Fact]
    public async Task Sin_token_devuelve_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            EventsUrl, new { events = new[] { new { eventType = "module_view", module = "tramites" } } },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Lote_valido_responde_202_con_los_aceptados()
    {
        var client = NewAuthenticatedClient();

        var response = await client.PostAsJsonAsync(EventsUrl, new
        {
            events = new object[]
            {
                new { eventType = "wizard_step_view", module = "tramites", stepKey = "comprador" },
                new { eventType = "wizard_step_complete", module = "tramites", stepKey = "comprador", durationMs = 1200 },
                new { eventType = "module_view", module = "reportes" },
            },
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<AcceptedResponse>(TestContext.Current.CancellationToken);
        body!.Accepted.Should().Be(3);
        BatchEvents().Should().HaveCount(3);
    }

    [Fact]
    public async Task Mas_de_50_eventos_devuelve_400_en_espanol()
    {
        var client = NewAuthenticatedClient();
        var events = Enumerable.Range(0, 51)
            .Select(_ => new { eventType = "module_view", module = "tramites" })
            .ToArray();

        var response = await client.PostAsJsonAsync(EventsUrl, new { events }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detail = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        detail.Should().Contain("50 eventos");
        BatchEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task EventType_desconocido_se_descarta_silenciosamente()
    {
        var client = NewAuthenticatedClient();

        var response = await client.PostAsJsonAsync(EventsUrl, new
        {
            events = new object[]
            {
                new { eventType = "module_view", module = "tramites" },
                new { eventType = "evento_inventado" },
                new { eventType = "wizard_complete", module = "tramites", durationMs = 90000 },
            },
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<AcceptedResponse>(TestContext.Current.CancellationToken);
        body!.Accepted.Should().Be(2);
        BatchEvents().Select(e => e.EventType)
            .Should().BeEquivalentTo(["module_view", "wizard_complete"]);
    }

    [Fact]
    public async Task Tenant_y_usuario_salen_del_jwt_no_del_body()
    {
        var client = NewAuthenticatedClient();

        var response = await client.PostAsJsonAsync(EventsUrl, new
        {
            // Intento de suplantación: el contrato no admite tenant en el body y se ignora.
            tenantId = Guid.NewGuid(),
            events = new object[]
            {
                new { eventType = "wizard_abandon", module = "tramites", stepKey = "fur" },
            },
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var record = BatchEvents().Should().ContainSingle().Subject;
        record.TenantId.Should().Be(TenantId);
        record.UserId.Should().Be(UserId);
    }

    [Fact]
    public async Task Sin_tenant_en_el_token_responde_202_sin_aceptar_nada()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(tenantId: null));

        var response = await client.PostAsJsonAsync(EventsUrl, new
        {
            events = new object[] { new { eventType = "module_view", module = "tramites" } },
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<AcceptedResponse>(TestContext.Current.CancellationToken);
        body!.Accepted.Should().Be(0);
        BatchEvents().Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // Infraestructura de la prueba
    // ------------------------------------------------------------------

    private sealed record AcceptedResponse(int Accepted);

    /// <summary>Eventos encolados por el ENDPOINT batch. Excluye los api_module_access que el
    /// middleware de telemetría del host real registra por el propio request de la prueba.</summary>
    private IReadOnlyList<UsageEventRecord> BatchEvents() =>
        [.. _factory.Queue.Snapshot().Where(e => e.EventType != "api_module_access")];

    private HttpClient NewAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(TenantId));
        return client;
    }

    /// <summary>JWT dummy (mismo patrón que Flit.Admin.Tests.TestTokenFactory): el host de pruebas
    /// no configura llave pública, así que el token se acepta sin validar firma.</summary>
    private static string CreateToken(Guid? tenantId)
    {
        var claims = new List<Claim>
        {
            new("sub", UserId.ToString()),
            new("role", "AdminCompany"),
        };
        if (tenantId is { } tenant)
            claims.Add(new Claim("tenant_id", tenant.ToString()));

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))),
                SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Host real con la cola de telemetría sustituida por una captura en memoria (así los
    /// asserts leen exactamente lo que encoló el endpoint, sin carreras con el writer).</summary>
    public sealed class TelemetryWebApplicationFactory : WebApplicationFactory<Program>
    {
        public CapturingUsageEventSink Queue { get; } = new();

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUsageEventQueue>();
                services.AddSingleton<IUsageEventQueue>(Queue);
            });
        }
    }

    public sealed class CapturingUsageEventSink : IUsageEventQueue
    {
        private readonly List<UsageEventRecord> _events = [];
        private readonly Lock _gate = new();

        public bool TryEnqueue(UsageEventRecord evt)
        {
            lock (_gate)
            {
                _events.Add(evt);
            }

            return true;
        }

        public IReadOnlyList<UsageEventRecord> Snapshot()
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _events.Clear();
            }
        }
    }
}
