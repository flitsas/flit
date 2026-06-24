using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Domain.Companies.VehicleOwnership;
using Flit.Admin.Domain.Companies.Whitelist;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Companies.Whitelist;

/// <summary>
/// Contrato HTTP del interceptor de propiedad vehicular (HU #10191) cableado en el
/// stub de radicación <c>POST /api/v1/internal/transfers/start</c>: AC1 (bloqueo 422
/// con el mensaje exacto) y AC2 (exención por whitelist → continúa) de extremo a
/// extremo. El checker de propiedad y el repositorio de whitelist se sustituyen por
/// dobles para no depender de PostgreSQL.
/// </summary>
public sealed class TransferStartEndpointTests : IClassFixture<TransferStartEndpointTests.TransferTestFactory>
{
    private const string StartUrl = "/api/v1/internal/transfers/start";

    private readonly TransferTestFactory _factory;

    public TransferStartEndpointTests(TransferTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task WithoutAuthorizationHeader_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(StartUrl, new
        {
            tenantId = Guid.NewGuid(),
            vehicleIdentifier = "ABC123",
            userEmail = "regular@co.com",
            onlyOwnVehicles = true,
        }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC1_Blocks_WithExactMessage_WhenVehicleNotOwnedAndNotWhitelisted()
    {
        var client = Authenticated();

        var response = await client.PostAsJsonAsync(StartUrl, new
        {
            tenantId = Guid.NewGuid(),
            vehicleIdentifier = "ABC123",
            userEmail = "regular@co.com",
            onlyOwnVehicles = true,
        }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(cancellationToken: TestContext.Current.CancellationToken);
        body!.Error.Should().Be("Vehículo no es propiedad de la compañía jurídica del tenant");
    }

    [Fact]
    public async Task AC2_Continues_WhenUserIsWhitelisted()
    {
        var client = Authenticated();

        var response = await client.PostAsJsonAsync(StartUrl, new
        {
            tenantId = Guid.NewGuid(),
            vehicleIdentifier = "ABC123",
            userEmail = "vip@co.com", // exento en el doble de whitelist
            onlyOwnVehicles = true,
        }, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient Authenticated()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateToken("Operador"));
        return client;
    }

    private sealed record ErrorBody(string Error);

    /// <summary>
    /// Factory que sustituye el checker de propiedad (siempre "no es del tenant") y la
    /// whitelist (solo <c>vip@co.com</c>) por dobles en memoria, evitando PostgreSQL.
    /// </summary>
    public sealed class TransferTestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IVehicleTenantOwnershipChecker>(_ => new NotOwnedChecker());
                services.AddScoped<IWhitelistRepository>(_ => new SingleEmailWhitelist("vip@co.com"));
            });
        }
    }

    private sealed class NotOwnedChecker : IVehicleTenantOwnershipChecker
    {
        public Task<bool> IsVehicleOwnedByTenantAsync(
            Guid tenantId, string vehicleIdentifier, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class SingleEmailWhitelist : IWhitelistRepository
    {
        private readonly string _email;

        public SingleEmailWhitelist(string email) => _email = WhitelistEmail.Normalize(email);

        public Task<bool> IsEmailWhitelistedAsync(
            Guid tenantId, string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(WhitelistEmail.Normalize(email) == _email);

        public Task<WhitelistAddOutcome> AddEmailsAsync(
            Guid tenantId, IReadOnlyList<string> normalizedEmails, Guid? addedBy, Guid? correlationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TenantWhitelistEntry>> ListAsync(
            Guid tenantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
