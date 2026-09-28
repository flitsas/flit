using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #12990 (FLIT Suite A-05) — servidor OIDC del hub contra la API real y PostgreSQL: emisor por host sellado,
/// authorization code con PKCE, JWKS, refresh, client credentials, login del hub y la bandera apagada.
/// </summary>
public sealed class OidcServerTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    internal const string Hub = "dev.flitsas.online";
    internal const string Callback = "https://dev.tramites.flitsas.online/auth/callback";
    internal const string ServiceSecret = "secreto-de-prueba-oidc";
    private const string Password = "OidcPass1!";

    private readonly WebApplicationFactory<Program> _base;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private string Email => $"oidc-{_suffix}@flit.local";

    public OidcServerTests(WebApplicationFactory<Program> factory)
    {
        _base = factory;
        _factory = WithOidc(factory);
        SeedAsync().GetAwaiter().GetResult();
    }

    internal static WebApplicationFactory<Program> WithOidc(WebApplicationFactory<Program> factory) =>
        factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Suite:Oidc:Enabled", "true");
            b.UseSetting("Suite:Hosts:Environment", "dev");
            b.UseSetting("Suite:Oidc:ServiceClients:svc-prueba:Secret", ServiceSecret);
            b.UseSetting("Suite:Oidc:ServiceClients:svc-prueba:Scopes:0", "platform.manifest");
        });

    private HttpClient NewClient(string host = Hub)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Flit-Domain", host);
        return client;
    }

    [Fact]
    public async Task Descubrimiento_ElEmisorEsElHubDelHostSellado()
    {
        var ct = TestContext.Current.CancellationToken;

        var hub = await NewClient().GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration", ct);
        hub.GetProperty("issuer").GetString().Should().Be($"https://{Hub}/");
        hub.GetProperty("jwks_uri").GetString().Should().Be($"https://{Hub}/.well-known/jwks.json");
        hub.GetProperty("authorization_endpoint").GetString().Should().Be($"https://{Hub}/connect/authorize");

        // Un host de producto o uno interno (un servicio que llama por la red de Docker) caen al hub del ambiente.
        var product = await NewClient("dev.tramites.flitsas.online").GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration", ct);
        product.GetProperty("issuer").GetString().Should().Be($"https://{Hub}/");
        var unsealed = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration", ct);
        unsealed.GetProperty("issuer").GetString().Should().Be($"https://{Hub}/");
    }

    [Fact]
    public async Task CodigoConPkce_EmiteTokenFirmadoValidableConElJwks_YRefresh()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = NewClient();
        (await LoginAsync(client, ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var tokens = await CodeFlowAsync(client, "tramites", ct);
        var accessToken = tokens.GetProperty("access_token").GetString()!;

        var jwt = new JsonWebToken(accessToken);
        jwt.Issuer.Should().Be($"https://{Hub}/");
        jwt.Audiences.Should().Equal("tramites");
        jwt.Subject.Should().Be(_userId.ToString());
        jwt.GetClaim("product").Value.Should().Be("tramites");
        jwt.GetClaim("tenant_id").Value.Should().Be(_tenantId.ToString());
        (jwt.ValidTo - jwt.IssuedAt).Should().BeCloseTo(TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(5));

        var jwks = new JsonWebKeySet(await client.GetStringAsync("/.well-known/jwks.json", ct));
        jwks.Keys.Should().ContainSingle(k => k.Kid == "flit-oidc-signing-v1");
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(accessToken, new TokenValidationParameters
        {
            ValidIssuer = $"https://{Hub}/", ValidAudience = "tramites", IssuerSigningKeys = jwks.GetSigningKeys(),
        });
        validation.IsValid.Should().BeTrue(validation.Exception?.Message);

        var refreshed = await RefreshAsync(client, tokens.GetProperty("refresh_token").GetString()!, ct);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LaLlaveDeFirmaSobreviveAUnReinicio()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = NewClient();
        await LoginAsync(client, ct);
        var accessToken = (await CodeFlowAsync(client, "tramites", ct)).GetProperty("access_token").GetString()!;

        using var restarted = WithOidc(new WebApplicationFactory<Program>());
        var other = restarted.CreateClient();
        other.DefaultRequestHeaders.Add("X-Flit-Domain", Hub);
        var jwks = new JsonWebKeySet(await other.GetStringAsync("/.well-known/jwks.json", ct));

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(accessToken, new TokenValidationParameters
        {
            ValidIssuer = $"https://{Hub}/", ValidAudience = "tramites", IssuerSigningKeys = jwks.GetSigningKeys(),
        });
        validation.IsValid.Should().BeTrue("la llave vive en security.jwt_signing_keys, no en memoria");
    }

    [Fact]
    public async Task SinSesion_AuthorizeLlevaAlLoginDelHubConElRetorno()
    {
        var response = await NewClient().GetAsync(AuthorizeUrl("tramites", "abc"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).Should().Be($"https://{Hub}/login");
        HttpUtility.ParseQueryString(location.Query)["returnUrl"].Should().StartWith("/connect/authorize?client_id=tramites");
    }

    [Fact]
    public async Task SinSesion_ConPromptNone_DevuelveLoginRequired()
    {
        var response = await NewClient().GetAsync(AuthorizeUrl("tramites", "abc") + "&prompt=none", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        HttpUtility.ParseQueryString(response.Headers.Location!.Query)["error"].Should().Be("login_required");
    }

    [Fact]
    public async Task Login_ConCredencialInvalida_401SinSesion()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = NewClient();
        var response = await client.PostAsJsonAsync("/connect/login", new { email = Email, password = "otra" }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("INVALID_CREDENTIALS");
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Theory]
    [InlineData("/connect/authorize?x=1", "/connect/authorize?x=1")]
    [InlineData("https://evil.example.com/", null)]
    [InlineData("//evil.example.com/", null)]
    [InlineData("/\\evil.example.com", null)]
    public async Task Login_SoloAceptaRetornosRelativosDelMismoHost(string returnUrl, string? expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await NewClient().PostAsJsonAsync("/connect/login", new { email = Email, password = Password, returnUrl }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        (body.GetProperty("returnUrl").ValueKind == JsonValueKind.Null ? null : body.GetProperty("returnUrl").GetString()).Should().Be(expected);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("flit_hub=", StringComparison.Ordinal));
        cookie.Should().Contain("httponly").And.Contain("samesite=lax").And.Contain("secure").And.NotContain("domain=");
    }

    [Fact]
    public async Task TokenDeServicio_ClientCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await NewClient().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = "svc-prueba", ["client_secret"] = ServiceSecret, ["scope"] = "platform.manifest",
        }), ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        var jwt = new JsonWebToken((await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("access_token").GetString());
        jwt.Subject.Should().Be("svc-prueba");
        jwt.Audiences.Should().Equal("plataforma");
        jwt.GetClaim("scope").Value.Should().Be("platform.manifest");
    }

    [Fact]
    public async Task TokenDeServicio_ScopeNoPermitido_SeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await NewClient().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = "svc-prueba", ["client_secret"] = ServiceSecret, ["scope"] = "platform.consultas",
        }), ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BanderaApagada_ConnectYDescubrimientoResponden404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _base.CreateClient();

        (await client.GetAsync("/.well-known/openid-configuration", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/connect/login", new { email = Email, password = Password }, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Emisores_IncluyenElHubDelAmbiente()
    {
        var issuers = await NewClient().GetFromJsonAsync<string[]>("/api/v1/platform/issuers", TestContext.Current.CancellationToken);

        issuers.Should().Contain($"https://{Hub}/");
    }

    internal static string AuthorizeUrl(string clientId, string challenge, string redirect = Callback) =>
        $"/connect/authorize?client_id={clientId}&response_type=code&scope=openid%20offline_access&redirect_uri={Uri.EscapeDataString(redirect)}&code_challenge={challenge}&code_challenge_method=S256&state=xyz";

    private Task<HttpResponseMessage> LoginAsync(HttpClient client, CancellationToken ct) =>
        client.PostAsJsonAsync("/connect/login", new { email = Email, password = Password }, ct);

    internal static async Task<JsonElement> CodeFlowAsync(HttpClient client, string clientId, CancellationToken ct, string redirect = Callback)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorize = await client.GetAsync(AuthorizeUrl(clientId, challenge, redirect), ct);
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect, await authorize.Content.ReadAsStringAsync(ct));
        var callback = authorize.Headers.Location!;
        var query = HttpUtility.ParseQueryString(callback.Query);
        query["error"].Should().BeNull(query["error_description"]);
        callback.GetLeftPart(UriPartial.Path).Should().Be(redirect);

        var token = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = query["code"]!, ["redirect_uri"] = redirect,
            ["client_id"] = clientId, ["code_verifier"] = verifier,
        }), ct);
        token.StatusCode.Should().Be(HttpStatusCode.OK, await token.Content.ReadAsStringAsync(ct));
        return await token.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    internal static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken, CancellationToken ct, string clientId = "tramites") =>
        client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken, ["client_id"] = clientId,
        }), ct);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Code = $"IT-OIDC-{_suffix}", LegalName = "Empresa OIDC", TaxId = TestNit.Unique(),
            TenantType = "RENTING", IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        db.Users.Add(new User { Id = _userId, Email = Email, DisplayName = "OIDC", Status = "active", HomeTenantId = _tenantId, CreatedAt = DateTimeOffset.UtcNow });
        db.UserCredentials.Add(new UserCredential { Id = Guid.CreateVersion7(), UserId = _userId, PasswordHash = hasher.Hash(Password), CreatedAt = DateTimeOffset.UtcNow });
        db.Roles.Add(new Role { Id = _roleId, Code = $"OidcRadicador-{_suffix}", Name = "Radicador OIDC", TargetEntityType = "COMPANY", ProductCode = "tramites", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        db.UserRoleAssignments.Add(new UserRoleAssignment { Id = Guid.CreateVersion7(), TenantId = _tenantId, UserId = _userId, RoleId = _roleId, AssignedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    public void Dispose()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        db.Database.ExecuteSql($"DELETE FROM identity.oidc_tokens WHERE subject = {_userId.ToString()}");
        db.Database.ExecuteSql($"DELETE FROM identity.oidc_authorizations WHERE subject = {_userId.ToString()}");
        db.UserRoleAssignments.Where(a => a.UserId == _userId).ExecuteDelete();
        db.Roles.Where(r => r.Id == _roleId).ExecuteDelete();
        db.UserCredentials.Where(c => c.UserId == _userId).ExecuteDelete();
        db.Set<TenantProductEntity>().Where(r => r.TenantId == _tenantId).ExecuteDelete();
        db.TenantConfigAuditLogs.Where(a => a.TenantId == _tenantId).ExecuteDelete();
        db.Users.Where(u => u.Id == _userId).ExecuteDelete();
        db.Tenants.Where(t => t.Id == _tenantId).ExecuteDelete();
    }
}
