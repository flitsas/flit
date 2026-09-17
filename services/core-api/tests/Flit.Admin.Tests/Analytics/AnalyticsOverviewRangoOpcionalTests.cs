using System.Net;
using System.Net.Http.Headers;
using Flit.Admin.Tests.Companies;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Analytics;

/// <summary>
/// BUG #12588 (defecto 4) — <c>from</c>/<c>to</c> pasan a ser OPCIONALES en
/// <c>GET /api/v1/analytics/overview</c>.
///
/// <para>
/// El dashboard enviaba siempre el mes en curso (<c>defaultRange</c>, AC2 de la HU #10247) y el
/// backend acota por fecha de CREACIÓN, así que todo lo radicado antes quedaba fuera de las
/// tarjetas aunque siguiera en curso. QA lo reportó como un total mal calculado; no lo era, el
/// dashboard contaba bien otro universo. Ahora arranca sin rango y el total es el real.
/// </para>
///
/// <para>
/// Lo que se fija aquí es la capa HTTP: que la ruta ENLACE sin esos parámetros. Con
/// <c>DateOnly</c> no anulable, omitirlos devolvía 400 antes de llegar al handler, y eso no lo
/// atrapa ninguna prueba de handler. El repositorio va sustituido: no hace falta PostgreSQL.
/// </para>
/// </summary>
public sealed class AnalyticsOverviewRangoOpcionalTests
    : IClassFixture<AnalyticsOverviewRangoOpcionalTests.RepoSubstituteFactory>
{
    private const string Url = "/api/v1/analytics/overview";
    private static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly RepoSubstituteFactory _factory;

    public AnalyticsOverviewRangoOpcionalTests(RepoSubstituteFactory factory)
    {
        _factory = factory;
        _factory.Repo.ClearReceivedCalls();
        _factory.Repo
            .GetOverviewAsync(Arg.Any<Guid?>(), Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(new List<CategoryMetricsDto>());
    }

    private HttpClient Client()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokenFactory.CreateAdminCompanyToken(TenantId));
        return client;
    }

    [Fact]
    public async Task SinFechas_Responde200YConsultaElUniversoCompleto()
    {
        var response = await Client().GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).GetOverviewAsync(
            Arg.Any<Guid?>(), null, null, Arg.Any<CancellationToken>());
    }

    [Theory] // Cada extremo acota por su lado; el otro queda abierto.
    [InlineData("?from=2026-06-01", "2026-06-01", null)]
    [InlineData("?to=2026-06-30", null, "2026-06-30")]
    public async Task UnSoloExtremo_Responde200YViajaSoloEseLado(string query, string? desde, string? hasta)
    {
        var response = await Client().GetAsync(Url + query, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).GetOverviewAsync(
            Arg.Any<Guid?>(),
            desde is null ? null : DateOnly.Parse(desde, System.Globalization.CultureInfo.InvariantCulture),
            hasta is null ? null : DateOnly.Parse(hasta, System.Globalization.CultureInfo.InvariantCulture),
            Arg.Any<CancellationToken>());
    }

    [Fact] // Regresión: el rango completo sigue funcionando igual que antes.
    public async Task ConRangoCompleto_SigueAcotando()
    {
        var response = await Client().GetAsync(
            Url + "?from=2026-06-01&to=2026-06-30", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).GetOverviewAsync(
            Arg.Any<Guid?>(), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), Arg.Any<CancellationToken>());
    }

    [Fact] // El par incoherente sigue siendo 400: es lo ÚNICO que el rango puede tener de inválido.
    public async Task ConParIncoherente_Responde400YNoConsulta()
    {
        var response = await Client().GetAsync(
            Url + "?from=2026-06-30&to=2026-06-01", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Repo.ReceivedCalls().Should().BeEmpty();
    }

    /// <summary>Host real con el repositorio de analítica sustituido (sin PostgreSQL).</summary>
    public sealed class RepoSubstituteFactory : WebApplicationFactory<Program>
    {
        public IAnalyticsReadRepository Repo { get; } = Substitute.For<IAnalyticsReadRepository>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services => services.AddScoped(_ => Repo));
        }
    }
}
