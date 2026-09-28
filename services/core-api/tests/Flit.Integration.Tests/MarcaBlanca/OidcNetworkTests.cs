using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Flit.Integration.Tests.MarcaBlanca;

/// <summary>
/// HU #12993 (FLIT Suite A-08) — login OIDC por el dominio de una red de Marca Blanca: el emisor y el claim <c>dom</c>
/// son el dominio de la red, el retorno solo va a dominios activos de esa misma red con el propósito del producto,
/// la sesión vale en los dominios de productos de la red y un usuario de la red no entra por el hub de FLIT.
/// </summary>
public sealed class OidcNetworkTests(PostgresDatabaseFixture fixture) : MarcaBlancaHttpTestBase(fixture)
{
    private const string TramitesHostA = "tramites.red-alfa-it.example";
    private static readonly string HubCallbackA = $"https://{MarcaBlancaScenario.HostA}/auth/callback";

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        if (!PostgresAvailability.IsAvailable)
            return;

        // Segundo dominio de la red A, para Trámites (B-08, purpose).
        await using var ctx = NewContext();
        var now = DateTimeOffset.UtcNow;
        ctx.TenantDomains.Add(new TenantDomainEntity
        {
            TenantId = MarcaBlancaScenario.HeadA, Host = TramitesHostA, Purpose = "tramites", VerificationToken = "tok-mb-red-alfa-tramites-03",
            Status = TenantDomainStatuses.Active, VerifiedAt = now, ActivatedAt = now, CertificateIssuedAt = now,
            CertificateExpiresAt = now.AddDays(90), CreatedAt = now, UpdatedAt = now,
        });
        await ctx.SaveChangesAsync();
    }

    [PostgresFact]
    public async Task PorElDominioDeLaRed_ElEmisorYDomSonElDominioDeLaRed()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = NewClient(MarcaBlancaScenario.HostA);

        var discovery = await client.GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration", ct);
        discovery.GetProperty("issuer").GetString().Should().Be($"https://{MarcaBlancaScenario.HostA}/");

        await LoginAsync(client, MarcaBlancaScenario.UserChildA, ct);
        var jwt = new JsonWebToken(await CodeFlowAsync(client, "plataforma", HubCallbackA, ct));

        jwt.Issuer.Should().Be($"https://{MarcaBlancaScenario.HostA}/");
        jwt.GetClaim("dom").Value.Should().Be(MarcaBlancaScenario.HostA);
    }

    [PostgresFact]
    public async Task ElRetornoNoVaAOtraRed_NiDesdeElHubDeFlitAUnaRed()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = NewClient(MarcaBlancaScenario.HostA);
        await LoginAsync(clientA, MarcaBlancaScenario.UserChildA, ct);

        (await clientA.GetAsync(AuthorizeUrl("plataforma", $"https://{MarcaBlancaScenario.HostB}/auth/callback"), ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "un retorno a la red B no es válido desde el hub de la red A");
        (await clientA.GetAsync(AuthorizeUrl("plataforma", $"https://{TramitesHostA}/auth/callback"), ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "el dominio de Trámites de la red no es el retorno del hub");

        var flit = NewClient(null);
        (await flit.GetAsync(AuthorizeUrl("plataforma", HubCallbackA), ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "desde el hub de FLIT no se vuelve a una red");
    }

    [PostgresFact]
    public async Task TramitesDeLaRed_VuelveASuDominioDeTramites_YLaSesionValeAhiPeroNoEnOtraRed()
    {
        var ct = TestContext.Current.CancellationToken;
        await GiveTramitesRoleAsync(MarcaBlancaScenario.UserChildA, MarcaBlancaScenario.ChildA);
        var client = NewClient(MarcaBlancaScenario.HostA);
        await LoginAsync(client, MarcaBlancaScenario.UserChildA, ct);

        var accessToken = await CodeFlowAsync(client, "tramites", $"https://{TramitesHostA}/auth/callback", ct);

        (await MeAsync(accessToken, TramitesHostA, ct)).StatusCode.Should().Be(HttpStatusCode.OK, "misma red, dominio de otro producto");
        var otherNetwork = await MeAsync(accessToken, MarcaBlancaScenario.HostB, ct);
        otherNetwork.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await otherNetwork.Content.ReadAsStringAsync(ct)).Should().Contain("SESSION_DOMAIN_MISMATCH");
    }

    [PostgresFact]
    public async Task UsuarioDeLaRed_PorElHubDeFlit_NETWORK_DOMAIN_REQUIRED()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await NewClient(null).PostAsJsonAsync("/connect/login",
            new { email = MarcaBlancaScenario.EmailOf(MarcaBlancaScenario.UserChildA), password = MarcaBlancaScenario.Password }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("error").GetString().Should().Be("NETWORK_DOMAIN_REQUIRED");
        body.GetProperty("loginUrl").GetString().Should().Be($"https://{MarcaBlancaScenario.HostA}/login");
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    private HttpClient NewClient(string? host)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        if (host is not null)
            client.DefaultRequestHeaders.Add("X-Flit-Domain", host);
        return client;
    }

    private static async Task LoginAsync(HttpClient client, Guid userId, CancellationToken ct) =>
        (await client.PostAsJsonAsync("/connect/login", new { email = MarcaBlancaScenario.EmailOf(userId), password = MarcaBlancaScenario.Password }, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

    private static string AuthorizeUrl(string clientId, string redirect, string challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM") =>
        $"/connect/authorize?client_id={clientId}&response_type=code&scope=openid&redirect_uri={Uri.EscapeDataString(redirect)}&code_challenge={challenge}&code_challenge_method=S256";

    private static async Task<string> CodeFlowAsync(HttpClient client, string clientId, string redirect, CancellationToken ct)
    {
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var authorize = await client.GetAsync(AuthorizeUrl(clientId, redirect, challenge), ct);
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect, await authorize.Content.ReadAsStringAsync(ct));
        authorize.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be(redirect);
        var code = HttpUtility.ParseQueryString(authorize.Headers.Location.Query)["code"];
        code.Should().NotBeNull(HttpUtility.ParseQueryString(authorize.Headers.Location.Query)["error_description"]);

        var token = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code!, ["redirect_uri"] = redirect, ["client_id"] = clientId, ["code_verifier"] = verifier,
        }), ct);
        token.StatusCode.Should().Be(HttpStatusCode.OK, await token.Content.ReadAsStringAsync(ct));
        return (await token.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("access_token").GetString()!;
    }

    private async Task<HttpResponseMessage> MeAsync(string accessToken, string host, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("X-Flit-Domain", host);
        return await Client.SendAsync(request, ct);
    }

    private async Task GiveTramitesRoleAsync(Guid userId, Guid tenantId)
    {
        await using var ctx = NewContext();
        var role = new Role { Id = Guid.NewGuid(), Code = $"MbRadicador-{Guid.NewGuid():N}"[..24], Name = "Radicador red", TargetEntityType = "COMPANY", ProductCode = "tramites", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        ctx.Roles.Add(role);
        await ctx.SaveChangesAsync();
        ctx.UserRoleAssignments.Add(new UserRoleAssignment { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, RoleId = role.Id, AssignedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
        await ctx.SaveChangesAsync();
    }
}
