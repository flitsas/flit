using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
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
/// HU #13085 — cuota por cliente externo sobre el host real (esquema externo, policies y el middleware en su
/// sitio del pipeline). Cada prueba usa su propio <c>client_id</c>: las particiones no se mezclan.
/// </summary>
public sealed class ExternalClientQuotaTests : IClassFixture<ExternalClientQuotaTests.Factory>
{
    private const string Url = "/api/v1/external/tramites/sync";

    private readonly Factory _factory;

    public ExternalClientQuotaTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task AC1_DentroDeLaCuotaSeAtiendeConNormalidad()
    {
        var cliente = Cliente(_factory, "cuota-ac1", ExternalScopes.Todos);

        for (var i = 1; i <= 101; i++)
        {
            (await cliente.GetAsync(Url, TestContext.Current.CancellationToken)).StatusCode
                .Should().Be(HttpStatusCode.OK, $"la solicitud {i} está dentro de 120 por minuto");
        }
    }

    [Fact]
    public async Task AC2_LaSolicitud121Recibe429ConRetryAfterEnProblemJson()
    {
        var cliente = Cliente(_factory, "cuota-ac2", ExternalScopes.Todos);
        for (var i = 1; i <= 120; i++)
        {
            (await cliente.GetAsync(Url, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var rechazada = await cliente.GetAsync(Url, TestContext.Current.CancellationToken);

        rechazada.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rechazada.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        rechazada.Headers.RetryAfter!.Delta!.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1))
            .And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
        var body = JsonDocument.Parse(await rechazada.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        body.GetProperty("code").GetString().Should().Be("rate_limited");
        body.GetProperty("status").GetInt32().Should().Be(429);
    }

    [Fact]
    public async Task AC2_LaCuotaEsPorClienteNoGlobal()
    {
        var agotado = Cliente(_factory, "cuota-agotado", ExternalScopes.Todos);
        for (var i = 1; i <= 121; i++)
        {
            await agotado.GetAsync(Url, TestContext.Current.CancellationToken);
        }

        (await Cliente(_factory, "cuota-otro", ExternalScopes.Todos).GetAsync(Url, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK, "otro cliente conserva su propia cuota");
    }

    [Fact]
    public async Task AC3_ConLaCuotaEn60LaSolicitud61Recibe429()
    {
        await using var factory = _factory.ConCuota(60);
        var cliente = Cliente(factory, "cuota-ac3", ExternalScopes.Todos);
        for (var i = 1; i <= 60; i++)
        {
            (await cliente.GetAsync(Url, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await cliente.GetAsync(Url, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task LosRechazosPorPermisoNoConsumenCuota()
    {
        await using var factory = _factory.ConCuota(3);
        var sinPermiso = Cliente(factory, "cuota-403", [ExternalScopes.TramitesPiiRead]);
        for (var i = 1; i <= 5; i++)
        {
            (await sinPermiso.GetAsync(Url, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var conPermiso = Cliente(factory, "cuota-403", ExternalScopes.Todos);
        for (var i = 1; i <= 3; i++)
        {
            (await conPermiso.GetAsync(Url, TestContext.Current.CancellationToken)).StatusCode
                .Should().Be(HttpStatusCode.OK, "los 403 anteriores del mismo client_id no gastaron la cuota");
        }
    }

    [Fact]
    public async Task ElPaseDeAccesoNoPasaPorLaCuotaDeCliente()
    {
        await using var factory = _factory.ConCuota(1);
        var cliente = Cliente(factory, "cuota-pase", ExternalScopes.Todos);
        (await cliente.GetAsync(Url, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        // El canje del pase es anónimo (sin client_id validado): lo limita external-token por IP, no esta cuota.
        using var canje = await factory.CreateClient().PostAsync("/api/v1/external/auth/token",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        canje.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    private static HttpClient Cliente(WebApplicationFactory<Program> factory, string clientId, IReadOnlyList<string> scopes)
    {
        using var scope = factory.Services.CreateScope();
        var pase = scope.ServiceProvider.GetRequiredService<IExternalClientTokenIssuer>().Issue(clientId, scopes).Token;
        var cliente = factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pase);
        return cliente;
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProcedureSyncReadRepository>();
                services.AddSingleton<IProcedureSyncReadRepository, SinCambios>();
            });

        public WebApplicationFactory<Program> ConCuota(int porMinuto) =>
            WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ExternalClients:RequestsPerClientPerMinute"] = porMinuto.ToString(System.Globalization.CultureInfo.InvariantCulture),
                })));
    }

    private sealed class SinCambios : IProcedureSyncReadRepository
    {
        public Task<IReadOnlyList<ProcedureSyncEntry>> ReadItemsAsync(ProcedureSyncPageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProcedureSyncEntry>>([]);

        public Task<IReadOnlyList<ProcedureSyncChange>> ReadChangesAsync(ProcedureSyncPageRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProcedureSyncInvoiceFile?> FindInvoiceAsync(Guid procedureId, Guid attachmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
