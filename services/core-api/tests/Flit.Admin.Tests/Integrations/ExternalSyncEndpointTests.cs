using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Flit.Admin.Domain.Integrations;
using Flit.Admin.Tests.Companies;
using Flit.Tramites.Domain.ExternalSync;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Flit.Admin.Tests.Integrations;

/// <summary>
/// HU #13081 — <c>GET /api/v1/external/tramites/sync</c> contra el host real (esquema externo, policies,
/// emisor de pases, serialización); el repositorio de lectura es un doble que devuelve una lista fija y
/// registra la página pedida. La lectura real contra Postgres está en <c>Flit.Integration.Tests</c>.
/// </summary>
public sealed class ExternalSyncEndpointTests : IClassFixture<ExternalSyncEndpointTests.Factory>
{
    private const string Url = "/api/v1/external/tramites/sync";

    private readonly Factory _factory;

    public ExternalSyncEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task AC1_RecorridoConCursorDevuelveLaSiguientePaginaYSuCursor()
    {
        _factory.Repo.Entradas = [Entrada(1), Entrada(2), Entrada(3)];
        var cursor = ExternalSyncCursor.Encode(new ProcedureSyncPosition(10, 5));

        var response = await Get($"{Url}?cursor={cursor}&pageSize=2", ExternalScopes.Todos);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await Json(response);
        body.GetProperty("items").GetArrayLength().Should().Be(2);
        body.GetProperty("hasMore").GetBoolean().Should().BeTrue("se pidió una fila de más y llegó");
        body.GetProperty("pageSize").GetInt32().Should().Be(2);
        body.GetProperty("serverTime").GetString().Should().EndWith("-05:00");
        ExternalSyncCursor.TryDecode(body.GetProperty("nextCursor").GetString(), out var siguiente, out _).Should().BeTrue();
        siguiente.Should().Be(Entrada(2).Position, "el cursor apunta al último ítem entregado");
        _factory.Repo.UltimaPeticion.Should().Be(new ProcedureSyncPageRequest(new(10, 5), null, 3, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task AC1_TodasLasClavesDelItemVanEnElJsonAunqueSeanNull()
    {
        _factory.Repo.Entradas = [Entrada(1)];

        var item = (await Json(await Get(Url, ExternalScopes.Todos))).GetProperty("items")[0];

        item.GetProperty("vehiculo").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("factura").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("fechaAprobacion").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("compradores")[0].GetProperty("correo").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task AC2_SinCambiosDevuelveItemsVacioYElMismoCursor()
    {
        _factory.Repo.Entradas = [];
        var cursor = ExternalSyncCursor.Encode(new ProcedureSyncPosition(10, 5));

        var body = await Json(await Get($"{Url}?cursor={cursor}", ExternalScopes.Todos));

        body.GetProperty("items").GetArrayLength().Should().Be(0);
        body.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        body.GetProperty("nextCursor").GetString().Should().Be(cursor);
    }

    [Fact]
    public async Task AC2_UnArranquePorFechaSinCambiosDevuelveUnCursorQueRecuerdaLaFecha()
    {
        _factory.Repo.Entradas = [];

        var body = await Json(await Get($"{Url}?since=2026-09-21T10:15:00-05:00", ExternalScopes.Todos));

        ExternalSyncCursor.TryDecode(body.GetProperty("nextCursor").GetString(), out var posicion, out var desde).Should().BeTrue();
        posicion.Should().BeNull();
        desde.Should().Be(new DateTimeOffset(2026, 9, 21, 10, 15, 0, TimeSpan.FromHours(-5)));
    }

    [Fact]
    public async Task AC3_SinPermisoDeDatosPersonalesLlegaEnmascarado()
    {
        _factory.Repo.Entradas = [Entrada(1)];

        var sinPii = (await Json(await Get(Url, [ExternalScopes.TramitesRead]))).GetProperty("items")[0].GetProperty("compradores")[0];
        var conPii = (await Json(await Get(Url, ExternalScopes.Todos))).GetProperty("items")[0].GetProperty("compradores")[0];

        sinPii.GetProperty("numeroDocumento").GetString().Should().Be("9****0000");
        sinPii.GetProperty("nombreCompleto").GetString().Should().Be("P*** E***");
        sinPii.GetProperty("direccion").GetString().Should().Be("***");
        sinPii.GetProperty("celular").GetString().Should().Be("******0000");
        sinPii.GetProperty("correo").ValueKind.Should().Be(JsonValueKind.Null, "un null sigue siendo null");
        conPii.GetProperty("numeroDocumento").GetString().Should().Be("900000000");
    }

    [Theory]
    [InlineData("?cursor=basura", "invalid_cursor")]
    [InlineData("?pageSize=0", "invalid_page_size")]
    [InlineData("?pageSize=1001", "invalid_page_size")]
    [InlineData("?pageSize=diez", "invalid_page_size")]
    [InlineData("?since=ayer", "invalid_since")]
    [InlineData("?since=09/30/2026", "invalid_since")]
    [InlineData("?since=2026-09-21T10:15:00", "invalid_since")]
    [InlineData("?since=2026-09-21T10:15:00-05:00&cursor=x", "cursor_and_since_exclusive")]
    public async Task AC4_ParametrosInvalidos400ConSuCodigo(string query, string code)
    {
        var response = await Get(Url + query, ExternalScopes.Todos);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await Json(response)).GetProperty("code").GetString().Should().Be(code);
    }

    [Fact]
    public async Task AC5_SinPaseOPaseDePlataforma401InvalidToken()
    {
        var sinPase = await _factory.CreateClient().GetAsync(Url, TestContext.Current.CancellationToken);
        using var conPlataforma = new HttpRequestMessage(HttpMethod.Get, Url);
        conPlataforma.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("SuperAdmin"));
        var plataforma = await _factory.CreateClient().SendAsync(conPlataforma, TestContext.Current.CancellationToken);

        foreach (var response in new[] { sinPase, plataforma })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            (await Json(response)).GetProperty("code").GetString().Should().Be("invalid_token");
        }
    }

    [Fact]
    public async Task AC5_SinPermisoDeLectura403InsufficientScope()
    {
        var response = await Get(Url, [ExternalScopes.TramitesPiiRead]);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await Json(response)).GetProperty("code").GetString().Should().Be("insufficient_scope");
    }

    private async Task<HttpResponseMessage> Get(string url, IReadOnlyList<string> scopes)
    {
        using var scope = _factory.Services.CreateScope();
        var pase = scope.ServiceProvider.GetRequiredService<IExternalClientTokenIssuer>().Issue("flito-test", scopes).Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pase);
        return await _factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

    private static ProcedureSyncEntry Entrada(int n) => new(
        new ProcedureSyncPosition(100, n),
        new ProcedureSyncItem(
            Guid.CreateVersion7(), $"FT1-000000{n}", n, n, DateTimeOffset.UtcNow, false, "entregado",
            new ProcedureSyncTramite("MATRICULA_NUEVA", "Matrícula inicial", "MATRICULAS"),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null,
            [new ProcedureSyncComprador(1, null, "comprador", "natural", "CC", "900000000", "PERSONA EJEMPLO",
                "CALLE 1 # 2-3", "PALMIRA", "3000000000", null)],
            null,
            new ProcedureSyncCompania(Guid.CreateVersion7(), "901000000", "TRAMITADORA EJEMPLO SAS")));

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public FakeSyncRepository Repo { get; } = new();

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProcedureSyncReadRepository>();
                services.AddSingleton<IProcedureSyncReadRepository>(Repo);
            });
    }

    public sealed class FakeSyncRepository : IProcedureSyncReadRepository
    {
        public IReadOnlyList<ProcedureSyncEntry> Entradas { get; set; } = [];

        public ProcedureSyncPageRequest? UltimaPeticion { get; private set; }

        public Task<IReadOnlyList<ProcedureSyncEntry>> ReadItemsAsync(ProcedureSyncPageRequest request, CancellationToken cancellationToken = default)
        {
            UltimaPeticion = request;
            return Task.FromResult<IReadOnlyList<ProcedureSyncEntry>>(Entradas.Take(request.PageSize).ToList());
        }

        public Task<IReadOnlyList<ProcedureSyncChange>> ReadChangesAsync(ProcedureSyncPageRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProcedureSyncInvoiceFile?> FindInvoiceAsync(Guid procedureId, Guid attachmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
