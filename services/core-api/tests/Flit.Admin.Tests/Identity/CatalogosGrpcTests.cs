using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #13459 — el catálogo de organismos de tránsito por gRPC solo lo lee un cliente de servicio de Identidad con el
/// scope <c>platform.tramites.catalogos</c> (aud=tramites), como svc-comparendos para su réplica.
/// </summary>
public sealed class CatalogosGrpcTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ConElScopeDeCatalogos_Entra()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        var token = await TokenAsync(host, "svc-comparendos-prueba", "platform.tramites.catalogos", ct);
        var respuesta = await ListarAsync(host, token, ct);
        respuesta.Organismos.Select(o => o.Codigo).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public async Task SinElScope_OSinToken_SeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();
        await RechazadoAsync(host, await TokenAsync(host, "svc-identidad-prueba", "platform.identidad.read", ct), ct);
        await RechazadoAsync(host, await TokenAsync(host, "svc-ict-prueba", "platform.tramites.ict", ct), ct);
        await RechazadoAsync(host, token: null, ct);
    }

    private WebApplicationFactory<Program> Host() =>
        OidcServerTests.WithOidc(factory).WithWebHostBuilder(b =>
        {
            foreach (var (cliente, scope) in new[] { ("svc-comparendos-prueba", "platform.tramites.catalogos"), ("svc-identidad-prueba", "platform.identidad.read"), ("svc-ict-prueba", "platform.tramites.ict") })
            {
                b.UseSetting($"Suite:Oidc:ServiceClients:{cliente}:Secret", OidcServerTests.ServiceSecret);
                b.UseSetting($"Suite:Oidc:ServiceClients:{cliente}:Scopes:0", scope);
            }
        });

    private static async Task<ListarOrganismosTransitoResponse> ListarAsync(WebApplicationFactory<Program> host, string? token, CancellationToken ct)
    {
        var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = host.Server.CreateHandler() });
        var invoker = token is null
            ? channel.CreateCallInvoker()
            : channel.CreateCallInvoker().Intercept(metadata =>
            {
                metadata.Add("authorization", $"Bearer {token}");
                return metadata;
            });
        return await new CatalogosService.CatalogosServiceClient(invoker).ListarOrganismosTransitoAsync(new ListarOrganismosTransitoRequest(), cancellationToken: ct);
    }

    private static async Task RechazadoAsync(WebApplicationFactory<Program> host, string? token, CancellationToken ct)
    {
        var call = () => ListarAsync(host, token, ct);
        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().BeOneOf(StatusCode.Unauthenticated, StatusCode.PermissionDenied);
    }

    private static async Task<string> TokenAsync(WebApplicationFactory<Program> host, string clientId, string scope, CancellationToken ct)
    {
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Flit-Domain", OidcServerTests.Hub);
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = OidcServerTests.ServiceSecret, ["scope"] = scope,
        }), ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("access_token").GetString()!;
    }
}
