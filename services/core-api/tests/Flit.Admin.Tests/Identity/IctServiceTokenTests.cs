using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Ict.Grpc.Contracts;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #13335 (Epic #13316) — el gRPC de ICT en core-api acepta el token de servicio de Identidad (svc-ict,
/// platform.tramites.ict) detrás de <c>Ict:ServiceToken:AcceptIdentity</c>, y deja de aceptar el HMAC compartido al
/// apagar <c>Ict:ServiceToken:AcceptLegacy</c> (el corte). La sonda es <c>IctConsultation.Query</c> sin empresa:
/// autorizada responde <c>invalid_tenant</c> sin tocar proveedores.
/// </summary>
public sealed class IctServiceTokenTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string HmacSecret = "secreto-hmac-de-prueba-ict-0123456789abcdef";

    [Fact]
    public async Task PorDefecto_ElHmacSigueEntrando_YElDeIdentidadNo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host();

        (await QueryAsync(host, LegacyToken(), ct)).ErrorCode.Should().Be("invalid_tenant");
        await RejectedAsync(host, await IdentityTokenAsync(host, "svc-ict-prueba", "platform.tramites.ict", ct), ct);
    }

    [Fact]
    public async Task ConAcceptIdentity_EntranLosDos()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host(("Ict:ServiceToken:AcceptIdentity", "true"));

        (await QueryAsync(host, await IdentityTokenAsync(host, "svc-ict-prueba", "platform.tramites.ict", ct), ct)).ErrorCode.Should().Be("invalid_tenant");
        (await QueryAsync(host, LegacyToken(), ct)).ErrorCode.Should().Be("invalid_tenant");
    }

    [Fact]
    public async Task TrasElCorte_ElHmacSeRechaza_YElDeIdentidadSigue()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host(("Ict:ServiceToken:AcceptIdentity", "true"), ("Ict:ServiceToken:AcceptLegacy", "false"));

        await RejectedAsync(host, LegacyToken(), ct);
        (await QueryAsync(host, await IdentityTokenAsync(host, "svc-ict-prueba", "platform.tramites.ict", ct), ct)).ErrorCode.Should().Be("invalid_tenant");
    }

    [Fact]
    public async Task OtroTokenDeServicio_SinElScopeDeIct_SeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = Host(("Ict:ServiceToken:AcceptIdentity", "true"));

        // aud=plataforma y sin platform.tramites.ict: un servicio cualquiera no puede orquestar ICT.
        await RejectedAsync(host, await IdentityTokenAsync(host, "svc-identidad-prueba", "platform.identidad.read", ct), ct);
        await RejectedAsync(host, token: null, ct);
    }

    private WebApplicationFactory<Program> Host(params (string Key, string Value)[] settings) =>
        OidcServerTests.WithOidc(factory).WithWebHostBuilder(b =>
        {
            b.UseSetting("Ict:ServiceToken:Secret", HmacSecret);
            b.UseSetting("Suite:Oidc:ServiceClients:svc-ict-prueba:Secret", OidcServerTests.ServiceSecret);
            b.UseSetting("Suite:Oidc:ServiceClients:svc-ict-prueba:Scopes:0", "platform.tramites.ict");
            b.UseSetting("Suite:Oidc:ServiceClients:svc-identidad-prueba:Secret", OidcServerTests.ServiceSecret);
            b.UseSetting("Suite:Oidc:ServiceClients:svc-identidad-prueba:Scopes:0", "platform.identidad.read");
            foreach (var (key, value) in settings)
                b.UseSetting(key, value);
        });

    private static async Task<ConsultationReply> QueryAsync(WebApplicationFactory<Program> host, string? token, CancellationToken ct)
    {
        var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = host.Server.CreateHandler() });
        var invoker = token is null
            ? channel.CreateCallInvoker()
            : channel.CreateCallInvoker().Intercept(metadata =>
            {
                metadata.Add("authorization", $"Bearer {token}");
                return metadata;
            });
        return await new IctConsultation.IctConsultationClient(invoker).QueryAsync(new ConsultationRequest(), cancellationToken: ct);
    }

    private static async Task RejectedAsync(WebApplicationFactory<Program> host, string? token, CancellationToken ct)
    {
        var call = () => QueryAsync(host, token, ct);
        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().BeOneOf(StatusCode.Unauthenticated, StatusCode.PermissionDenied);
    }

    private static async Task<string> IdentityTokenAsync(WebApplicationFactory<Program> host, string clientId, string scope, CancellationToken ct)
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

    /// <summary>El JWT HMAC que firma hoy core-ict (IctServiceTokenProvider), con los valores por defecto.</summary>
    private static string LegacyToken() =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "flit-ict-svc",
            Audience = "flit-internal",
            Subject = new ClaimsIdentity([new Claim("sub", "core-ict"), new Claim("scope", "ict.orchestration")]),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(HmacSecret)), SecurityAlgorithms.HmacSha256),
        });
}
