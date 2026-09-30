using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Domain.Integrations;
using Flit.Tramites.Domain.ExternalSync;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Flit.Admin.Tests.Integrations;

/// <summary>
/// HU #13086 — bitácora de <c>/api/v1/external/*</c> sobre el host real (middleware en su sitio del pipeline,
/// esquema externo, límites de tasa). La bitácora, el feed y los clientes son dobles en memoria; la inserción
/// real contra Postgres está en <c>Flit.Integration.Tests</c>. Cada prueba usa su propio <c>client_id</c>.
/// </summary>
public sealed class ExternalAccessLogTests : IClassFixture<ExternalAccessLogTests.Factory>
{
    private const string Url = "/api/v1/external/tramites/sync";
    private static readonly Guid CompaniaA = Guid.CreateVersion7();
    private static readonly Guid CompaniaB = Guid.CreateVersion7();

    private readonly Factory _factory;

    public ExternalAccessLogTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task AC1_UnaPaginaQuedaRegistradaConLoQueSeEntrego()
    {
        _factory.Feed.Entradas = [Entrada(7, CompaniaA), Entrada(8, CompaniaB), Entrada(9, CompaniaA)];
        using var request = Peticion(Url, "bitacora-ac1", ExternalScopes.Todos);
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");

        (await _factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var fila = _factory.Bitacora.Unica("bitacora-ac1");
        fila.Endpoint.Should().Be("tramites.sync");
        fila.HttpStatus.Should().Be(200);
        fila.SyncVersionFrom.Should().Be(7);
        fila.SyncVersionTo.Should().Be(9);
        fila.ItemsCount.Should().Be(3);
        fila.TenantIds.Should().BeEquivalentTo([CompaniaA, CompaniaB], "compañías tocadas, sin repetir");
        fila.PiiUnmasked.Should().BeTrue("el pase trae external.tramites.pii.read y hubo compradores");
        fila.Ip.Should().Be(IPAddress.Parse("203.0.113.7"));
        fila.DurationMs.Should().BeGreaterThanOrEqualTo(0);
        fila.RequestId.Should().NotBeNullOrEmpty();
        fila.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task AC1_SinPermisoDeDatosPersonalesYPaginaVacia()
    {
        _factory.Feed.Entradas = [Entrada(1, CompaniaA)];
        await Enviar(Peticion(Url, "bitacora-enmascarado", [ExternalScopes.TramitesRead]));
        _factory.Feed.Entradas = [];
        await Enviar(Peticion(Url, "bitacora-vacia", ExternalScopes.Todos));

        _factory.Bitacora.Unica("bitacora-enmascarado").PiiUnmasked.Should().BeFalse("llegó enmascarado");
        var vacia = _factory.Bitacora.Unica("bitacora-vacia");
        vacia.ItemsCount.Should().Be(0);
        vacia.SyncVersionFrom.Should().BeNull();
        vacia.SyncVersionTo.Should().BeNull();
        vacia.PiiUnmasked.Should().BeFalse("no se entregó ningún dato personal");
    }

    [Fact]
    public async Task AC2_LosRechazos401Y403QuedanRegistrados()
    {
        using var sinPase = new HttpRequestMessage(HttpMethod.Get, Url);
        sinPase.Headers.Add("X-Forwarded-For", "203.0.113.41");
        await Enviar(sinPase);
        await Enviar(Peticion(Url, "bitacora-403", [ExternalScopes.TramitesPiiRead]));

        var anonima = _factory.Bitacora.Filas.Single(f => Equals(f.Ip, IPAddress.Parse("203.0.113.41")));
        anonima.ClientId.Should().BeNull("no trajo pase válido");
        anonima.HttpStatus.Should().Be(401);
        anonima.Endpoint.Should().Be("tramites.sync");
        _factory.Bitacora.Unica("bitacora-403").HttpStatus.Should().Be(403);
    }

    [Fact]
    public async Task AC2_ElRechazoPorCuota429QuedaRegistrado()
    {
        await using var factory = _factory.ConCuota(1);
        _factory.Feed.Entradas = [];
        var cliente = factory.CreateClient();
        foreach (var _ in new[] { 1, 2 })
        {
            using var request = Peticion(factory, Url, "bitacora-429", ExternalScopes.Todos);
            await cliente.SendAsync(request, TestContext.Current.CancellationToken);
        }

        _factory.Bitacora.De("bitacora-429").Select(f => f.HttpStatus).Should().Equal(200, 429);
    }

    [Fact]
    public async Task AC2_ElCanjeDelPaseRegistraElClienteSolicitado401Y423()
    {
        _factory.Clientes.Bloqueado("bitacora-bloqueado");

        await Enviar(Canje("bitacora-desconocido"));
        await Enviar(Canje("bitacora-bloqueado"));

        var invalido = _factory.Bitacora.Unica("bitacora-desconocido");
        invalido.Endpoint.Should().Be("token");
        invalido.HttpStatus.Should().Be(401);
        _factory.Bitacora.Unica("bitacora-bloqueado").HttpStatus.Should().Be(423);
    }

    [Fact]
    public async Task AC3_NoSeGuardanSecretosPasesNiTextoLibre()
    {
        _factory.Feed.Entradas = [Entrada(1, CompaniaA)];
        using var request = Peticion(Url, "bitacora-ac3", ExternalScopes.Todos);
        var pase = request.Headers.Authorization!.Parameter!;
        await Enviar(request);
        using var canjeConCorreo = Canje("persona@ejemplo.test", secreto: "secreto-que-no-debe-quedar");
        await Enviar(canjeConCorreo);

        var textos = _factory.Bitacora.Filas
            .SelectMany(f => new[] { f.ClientId, f.Endpoint, f.RequestId })
            .Where(t => t is not null)
            .ToList();
        textos.Should().NotContain(t => t!.Contains(pase, StringComparison.Ordinal), "el pase no se guarda");
        textos.Should().NotContain(t => t!.Contains("secreto-que-no-debe-quedar", StringComparison.Ordinal));
        textos.Should().NotContain(t => t!.Contains('@'), "un client_id con otro formato (p. ej. un correo) no se guarda");
        _factory.Bitacora.Filas.Should().Contain(f => f.Endpoint == "token" && f.ClientId == null && f.HttpStatus == 401);
        typeof(ExternalAccessLogEntry).GetProperties().Select(p => p.Name).Should().NotContain(
            n => n.Contains("Body", StringComparison.Ordinal) || n.Contains("Token", StringComparison.Ordinal),
            "la fila no tiene dónde guardar cuerpos ni pases");
    }

    [Fact]
    public async Task UnaFallaDeLaBitacoraNoAfectaLaRespuesta()
    {
        _factory.Feed.Entradas = [];
        _factory.Bitacora.Falla = true;
        try
        {
            using var request = Peticion(Url, "bitacora-falla", ExternalScopes.Todos);
            (await _factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            _factory.Bitacora.Falla = false;
        }
    }

    [Fact]
    public async Task LasRutasFueraDelPrefijoExternoNoSeRegistran()
    {
        var antes = _factory.Bitacora.Filas.Count;

        await _factory.CreateClient().GetAsync("/api/v1/health", TestContext.Current.CancellationToken);

        _factory.Bitacora.Filas.Count.Should().Be(antes);
    }

    private HttpRequestMessage Peticion(string url, string clientId, IReadOnlyList<string> scopes) =>
        Peticion(_factory, url, clientId, scopes);

    private static HttpRequestMessage Peticion(WebApplicationFactory<Program> factory, string url, string clientId, IReadOnlyList<string> scopes)
    {
        using var scope = factory.Services.CreateScope();
        var pase = scope.ServiceProvider.GetRequiredService<IExternalClientTokenIssuer>().Issue(clientId, scopes).Token;
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pase);
        return request;
    }

    private static HttpRequestMessage Canje(string clientId, string secreto = "secreto-equivocado")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/external/auth/token")
        {
            Content = JsonContent.Create(new { clientId, clientSecret = secreto }),
        };
        request.Headers.Add("X-Forwarded-For", $"198.51.100.{Random.Shared.Next(1, 255)}");
        return request;
    }

    private async Task Enviar(HttpRequestMessage request)
    {
        using (request)
        {
            await _factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
        }
    }

    private static ProcedureSyncEntry Entrada(long version, Guid compania) => new(
        new ProcedureSyncPosition(100, version),
        new ProcedureSyncItem(
            Guid.CreateVersion7(), $"FT1-{version:D7}", version, version, DateTimeOffset.UtcNow, false, "entregado",
            new ProcedureSyncTramite("MATRICULA_NUEVA", "Matrícula inicial", "MATRICULAS"),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null,
            [new ProcedureSyncComprador(1, null, "comprador", "natural", "CC", "900000000", "PERSONA EJEMPLO",
                "CALLE 1 # 2-3", "PALMIRA", "3000000000", null)],
            null,
            new ProcedureSyncCompania(compania, "901000000", "TRAMITADORA EJEMPLO SAS")));

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public BitacoraEnMemoria Bitacora { get; } = new();

        public FeedFijo Feed { get; } = new();

        public ClientesEnMemoria Clientes { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IExternalAccessLogRepository>();
                services.AddSingleton<IExternalAccessLogRepository>(Bitacora);
                services.RemoveAll<IProcedureSyncReadRepository>();
                services.AddSingleton<IProcedureSyncReadRepository>(Feed);
                services.RemoveAll<IExternalClientRepository>();
                services.AddSingleton<IExternalClientRepository>(Clientes);
            });

        public WebApplicationFactory<Program> ConCuota(int porMinuto) =>
            WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ExternalClients:RequestsPerClientPerMinute"] = porMinuto.ToString(System.Globalization.CultureInfo.InvariantCulture),
                })));
    }

    public sealed class BitacoraEnMemoria : IExternalAccessLogRepository
    {
        private readonly ConcurrentQueue<ExternalAccessLogEntry> _filas = new();

        public bool Falla { get; set; }

        public IReadOnlyList<ExternalAccessLogEntry> Filas => _filas.ToList();

        public IReadOnlyList<ExternalAccessLogEntry> De(string clientId) => Filas.Where(f => f.ClientId == clientId).ToList();

        public ExternalAccessLogEntry Unica(string clientId) => De(clientId).Should().ContainSingle().Subject;

        public Task AddAsync(ExternalAccessLogEntry entry, CancellationToken cancellationToken = default)
        {
            if (Falla)
            {
                throw new InvalidOperationException("base caída");
            }

            _filas.Enqueue(entry);
            return Task.CompletedTask;
        }
    }

    public sealed class FeedFijo : IProcedureSyncReadRepository
    {
        public IReadOnlyList<ProcedureSyncEntry> Entradas { get; set; } = [];

        public Task<IReadOnlyList<ProcedureSyncEntry>> ReadItemsAsync(ProcedureSyncPageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProcedureSyncEntry>>(Entradas.Take(request.PageSize).ToList());

        public Task<IReadOnlyList<ProcedureSyncChange>> ReadChangesAsync(ProcedureSyncPageRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProcedureSyncInvoiceFile?> FindInvoiceAsync(Guid procedureId, Guid attachmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    public sealed class ClientesEnMemoria : IExternalClientRepository
    {
        private readonly ConcurrentDictionary<string, ExternalClientCredentials> _clientes = new(StringComparer.Ordinal);

        public void Bloqueado(string clientId) =>
            _clientes[clientId] = new ExternalClientCredentials(
                Guid.CreateVersion7(), clientId, "hash", null, null, false, true, [ExternalScopes.TramitesRead], 0,
                DateTimeOffset.UtcNow.AddMinutes(15));

        public Task<ExternalClientCredentials?> GetCredentialsByClientIdAsync(string clientId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_clientes.TryGetValue(clientId, out var c) ? c : null);

        public Task<DateTimeOffset?> RegisterFailedAttemptAsync(
            Guid id, int maxFailedAttempts, TimeSpan lockDuration, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult<DateTimeOffset?>(null);

        public Task RegisterTokenIssuedAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ExternalClientView> CreateAsync(NewExternalClient client, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ExternalClientView?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ExternalClientView>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
