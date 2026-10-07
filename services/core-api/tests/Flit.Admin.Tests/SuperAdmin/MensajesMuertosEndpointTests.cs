using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Notificaciones.Grpc.V1;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.SuperAdmin;

/// <summary>
/// HU #13357 (Epic #13316) — <c>/api/v1/superadmin/notificaciones/mensajes-muertos</c>: el SuperAdmin lista, reintenta y
/// descarta (reintentar y descartar quedan en la auditoría); un AdminCompany recibe 403 (AC2). core-notificaciones se
/// reemplaza por un CallInvoker falso; que el mensaje reintentado vuelve a su cola y se procesa (AC1) lo prueban
/// core-notificaciones y el SDK contra el broker real.
/// </summary>
public sealed class MensajesMuertosEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Base = "/api/v1/superadmin/notificaciones/mensajes-muertos";
    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes("clave-de-prueba-de-al-menos-32-bytes!!"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SuperAdmin_VeLosMensajesMuertosDeUnaCola_ConSuUltimoError()
    {
        var invoker = new NotificacionesFalso();
        var response = await Client(invoker, "SuperAdmin").GetAsync($"{Base}?cola=correos", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var m = cuerpo.GetProperty("mensajes")[0];
        m.GetProperty("tipo").GetString().Should().Be("notificaciones.email.send");
        m.GetProperty("ultimoError").GetString().Should().Be("El correo no salió (ProviderUnavailable).");
        m.GetProperty("origen").GetString().Should().Be("tramites");
        invoker.Pedidos.OfType<ListarMensajesMuertosRequest>().Single().Cola.Should().Be(ColaMuertos.Correos);
        invoker.Empresas.Should().OnlyContain(e => !string.IsNullOrEmpty(e), "la llamada de servicio lleva una empresa");
    }

    [Fact]
    public async Task SuperAdmin_ReintentaYDescarta_YQuedaEnLaAuditoria()
    {
        var invoker = new NotificacionesFalso();
        var auditoria = new AuditoriaFalsa();
        var client = Client(invoker, "SuperAdmin", auditoria: auditoria);
        var reintentado = Guid.NewGuid();
        var descartado = Guid.NewGuid();

        (await client.PostAsync($"{Base}/webhooks/{reintentado}/reintentar", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostAsync($"{Base}/correos/{descartado}/descartar", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        invoker.Pedidos.OfType<ReintentarMensajeMuertoRequest>().Single().Should().BeEquivalentTo(new { Cola = ColaMuertos.Webhooks, Id = reintentado.ToString() });
        invoker.Pedidos.OfType<DescartarMensajeMuertoRequest>().Single().Should().BeEquivalentTo(new { Cola = ColaMuertos.Correos, Id = descartado.ToString() });
        auditoria.Entradas.Where(a => a.TargetEntityId == reintentado || a.TargetEntityId == descartado).Select(a => a.Operation)
            .Should().BeEquivalentTo("retry_dead_letter", "discard_dead_letter");
        auditoria.Entradas.Should().OnlyContain(a => a.Module == "notifications" && a.Result == "success" && a.TargetEntityType == "DEAD_LETTER");
    }

    [Fact]
    public async Task UnMensajeQueYaNoEsta_404()
    {
        var response = await Client(new NotificacionesFalso { NoEsta = true }, "SuperAdmin").PostAsync($"{Base}/correos/{Guid.NewGuid()}/descartar", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("GET", "?cola=correos")]
    [InlineData("POST", "/correos/00000000-0000-0000-0000-000000000001/reintentar")]
    [InlineData("POST", "/correos/00000000-0000-0000-0000-000000000001/descartar")]
    public async Task AC2_AdminCompany_Recibe403(string metodo, string ruta)
    {
        var invoker = new NotificacionesFalso();
        var response = await Client(invoker, "AdminCompany").SendAsync(new HttpRequestMessage(new HttpMethod(metodo), Base + ruta), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        invoker.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task SinNotificacionesEnElAmbiente_503ConSuCodigo()
    {
        var response = await Client(invoker: null, "SuperAdmin").GetAsync($"{Base}?cola=correos", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("NOTIFICACIONES_NO_CONFIGURADO");
    }

    private HttpClient Client(NotificacionesFalso? invoker, string role, AuditoriaFalsa? auditoria = null)
    {
        var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            if (invoker is not null)
                s.AddSingleton(new MensajesMuertosService.MensajesMuertosServiceClient(invoker));
            if (auditoria is not null)
                s.AddScoped<IAdminAuditWriter>(_ => auditoria);
        }));
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(role));
        return client;
    }

    private static string Token(string role) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = "https://api.flit.co",
        Audience = "flit-api",
        Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", role), new Claim("tenant_id", Guid.NewGuid().ToString())]),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
    });

    /// <summary>Guarda lo que el filtro de auditoría escribiría (la escritura real la cubren las pruebas de auditoría).</summary>
    private sealed class AuditoriaFalsa : IAdminAuditWriter
    {
        public List<AdminAuditEntry> Entradas { get; } = [];

        public Task WriteAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
        {
            lock (Entradas)
                Entradas.Add(entry);
            return Task.CompletedTask;
        }
    }

    /// <summary>Hace de core-notificaciones: guarda lo que le piden y responde un mensaje muerto de ejemplo.</summary>
    private sealed class NotificacionesFalso : CallInvoker
    {
        public bool NoEsta { get; init; }

        public List<object> Pedidos { get; } = [];

        public List<string?> Empresas { get; } = [];

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            lock (Pedidos)
            {
                Pedidos.Add(request);
                Empresas.Add(options.Headers?.GetValue("x-flit-tenant-id"));
            }

            object respuesta = request switch
            {
                ListarMensajesMuertosRequest => new ListarMensajesMuertosResponse
                {
                    Mensajes =
                    {
                        new MensajeMuerto
                        {
                            Id = Guid.NewGuid().ToString(), Tipo = "notificaciones.email.send", Productor = "tramites", Intentos = 3,
                            MuertoEn = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow), UltimoError = "El correo no salió (ProviderUnavailable).",
                        },
                    },
                },
                ReintentarMensajeMuertoRequest => new ReintentarMensajeMuertoResponse(),
                _ => new DescartarMensajeMuertoResponse(),
            };
            var tarea = NoEsta
                ? Task.FromException<TResponse>(new RpcException(new Status(StatusCode.NotFound, "no está")))
                : Task.FromResult((TResponse)respuesta);
            return new AsyncUnaryCall<TResponse>(tarea, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }
}
