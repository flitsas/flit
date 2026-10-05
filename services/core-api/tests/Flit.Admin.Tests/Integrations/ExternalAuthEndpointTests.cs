using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Admin.Domain.Integrations;
using Flit.Admin.Tests.Companies;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Integrations;

/// <summary>
/// HU #13087 — <c>POST /api/v1/external/auth/token</c> contra el host real (esquemas, policies, límite
/// por IP y hasher Argon2id reales); solo el repositorio es un doble en memoria. Cada petición lleva su
/// propia IP en <c>X-Forwarded-For</c> para que el límite de 10/min no mezcle pruebas.
/// </summary>
public sealed class ExternalAuthEndpointTests : IClassFixture<ExternalAuthEndpointTests.Factory>
{
    private const string TokenUrl = "/api/v1/external/auth/token";
    private const string PlatformUrl = "/api/v1/admin/rejection-reasons";
    private const string Secreto = "secreto-de-prueba-0123456789"; // gitleaks:allow — valor de prueba

    private readonly Factory _factory;

    public ExternalAuthEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task AC1_PaseValido_200ConBearer1800YPermisos()
    {
        var clientId = _factory.Alta(scopes: [ExternalScopes.TramitesRead, ExternalScopes.TramitesPiiRead]);

        var response = await PedirPase(clientId, Secreto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("tokenType").GetString().Should().Be("Bearer");
        body.GetProperty("expiresIn").GetInt32().Should().Be(1800);
        body.GetProperty("scope").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(ExternalScopes.TramitesRead, ExternalScopes.TramitesPiiRead);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.GetProperty("accessToken").GetString());
        jwt.Header.Alg.Should().Be("RS256");
        jwt.Issuer.Should().Be("flit-core-external");
        jwt.Audiences.Should().Equal("flit-external");
        jwt.Subject.Should().Be(clientId);
        jwt.Claims.Where(c => c.Type == "scope").Select(c => c.Value)
            .Should().Equal(ExternalScopes.TramitesRead, ExternalScopes.TramitesPiiRead);
        jwt.Claims.Should().NotContain(c => c.Type == "tenant_id" || c.Type == "role");
        (jwt.ValidTo - jwt.ValidFrom).Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task AC2_SecretoIncorrectoYClienteInactivo_MismoInvalidClient()
    {
        var activo = _factory.Alta();
        var inactivo = _factory.Alta(isActive: false);

        var incorrecto = await PedirPase(activo, "secreto-equivocado");
        var desactivado = await PedirPase(inactivo, Secreto);
        var inexistente = await PedirPase("no-existe-nadie", Secreto);

        foreach (var response in new[] { incorrecto, desactivado, inexistente })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        }

        var cuerpos = await Task.WhenAll(new[] { incorrecto, desactivado, inexistente }
            .Select(r => r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
        cuerpos.Distinct().Should().ContainSingle("no se revela cuál de las causas falló");
        JsonDocument.Parse(cuerpos[0]).RootElement.GetProperty("code").GetString().Should().Be("invalid_client");
    }

    [Fact]
    public async Task AC3_TrasCincoFallos_423ConRetryAfterAunqueElSecretoSeaCorrecto()
    {
        var clientId = _factory.Alta();

        for (var i = 0; i < 5; i++)
        {
            (await PedirPase(clientId, "secreto-equivocado")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var response = await PedirPase(clientId, Secreto);

        response.StatusCode.Should().Be((HttpStatusCode)423);
        response.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeInRange(1, 15 * 60);
        (await Code(response)).Should().Be("client_locked");
    }

    [Fact]
    public async Task AC4_RotacionObligatoria_403SecretRotationRequired()
    {
        var clientId = _factory.Alta(mustRotate: true);

        var response = await PedirPase(clientId, Secreto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Code(response)).Should().Be("secret_rotation_required");
    }

    [Fact]
    public async Task AC4_TrasRotar_ElSecretoAnteriorSigueValiendoEnLaVentana()
    {
        var clientId = _factory.Alta(secret: "secreto-nuevo-0123456789", previousSecret: Secreto, // gitleaks:allow — valor de prueba
            rotatedAt: DateTimeOffset.UtcNow.AddHours(-1));

        (await PedirPase(clientId, Secreto)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PedirPase(clientId, "secreto-nuevo-0123456789")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AC5_ElPaseExternoNoSirveEnLaPlataforma()
    {
        var pase = await PaseExterno();

        using var request = new HttpRequestMessage(HttpMethod.Get, PlatformUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pase);
        var response = await _factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Autenticar(JwtBearerDefaults.AuthenticationScheme, pase)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task AC5_ElPaseExternoAutenticaYCumpleLaPolicyDeLectura()
    {
        var pase = await PaseExterno();

        var result = await Autenticar(ExternalClientAuthorization.Scheme, pase);

        result.Succeeded.Should().BeTrue();
        (await Autorizar(result.Principal!, ExternalClientAuthorization.TramitesReadPolicy)).Should().BeTrue();
    }

    [Fact]
    public async Task AC5_PasesDePlataformaYDeIctNoSirvenEnElEsquemaExterno()
    {
        var plataforma = TestTokenFactory.CreateToken("SuperAdmin");
        var ict = PaseIct();

        foreach (var pase in new[] { plataforma, ict })
        {
            var result = await Autenticar(ExternalClientAuthorization.Scheme, pase);
            result.Succeeded.Should().BeFalse();
        }
    }

    [Fact]
    public async Task AC5_UnPaseSinPermisoDeLecturaNoCumpleLaPolicy()
    {
        var pase = await PaseExterno([ExternalScopes.TramitesPiiRead]);

        var result = await Autenticar(ExternalClientAuthorization.Scheme, pase);

        result.Succeeded.Should().BeTrue();
        (await Autorizar(result.Principal!, ExternalClientAuthorization.TramitesReadPolicy)).Should().BeFalse();
    }

    [Fact]
    public async Task AC6_LaUndecimaPeticionDelMinutoDesdeLaMismaIp_429ConRetryAfter()
    {
        var ip = NuevaIp();

        for (var i = 0; i < 10; i++)
        {
            (await PedirPase("no-existe-nadie", Secreto, ip)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var response = await PedirPase("no-existe-nadie", Secreto, ip);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeInRange(1, 60);
        (await Code(response)).Should().Be("rate_limited");

        (await PedirPase("no-existe-nadie", Secreto, NuevaIp())).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "otra IP tiene su propio cupo");
    }

    private async Task<HttpResponseMessage> PedirPase(string clientId, string secret, string? ip = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = JsonContent.Create(new { clientId, clientSecret = secret }),
        };
        request.Headers.Add("X-Forwarded-For", ip ?? NuevaIp());
        return await _factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<string> PaseExterno(IReadOnlyList<string>? scopes = null)
    {
        var response = await PedirPase(_factory.Alta(scopes: scopes), Secreto);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("accessToken").GetString()!;
    }

    private async Task<AuthenticateResult> Autenticar(string scheme, string token)
    {
        using var scope = _factory.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = $"Bearer {token}";
        return await context.AuthenticateAsync(scheme);
    }

    private async Task<bool> Autorizar(ClaimsPrincipal principal, string policy)
    {
        using var scope = _factory.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        return (await authorization.AuthorizeAsync(principal, null, policy)).Succeeded;
    }

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("code").GetString();
    }

    /// <summary>Pase con la forma de uno de ICT (emisor, audiencia y llave de ICT).</summary>
    private static string PaseIct()
    {
        using var rsa = RSA.Create(2048);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://ict.flit.co",
            Audience = "flit-ict",
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString()), new Claim("scope", ExternalScopes.TramitesRead)]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256),
        });
    }

    private static string NuevaIp() => $"10.{Random.Shared.Next(256)}.{Random.Shared.Next(256)}.{Random.Shared.Next(1, 255)}";

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly InMemoryExternalClients _clients = new();

        /// <summary>Da de alta un cliente con identificador único y devuelve su <c>client_id</c>.</summary>
        public string Alta(
            string secret = Secreto,
            string? previousSecret = null,
            DateTimeOffset? rotatedAt = null,
            bool mustRotate = false,
            bool isActive = true,
            IReadOnlyList<string>? scopes = null)
        {
            using var scope = Services.CreateScope();
            var hasher = scope.ServiceProvider.GetRequiredService<IExternalClientSecretHasher>();
            var clientId = $"test-{Guid.NewGuid():N}"[..20];
            _clients.Put(new ExternalClientCredentials(
                Guid.CreateVersion7(), clientId, hasher.Hash(secret),
                previousSecret is null ? null : hasher.Hash(previousSecret), rotatedAt, mustRotate, isActive,
                scopes ?? [ExternalScopes.TramitesRead], 0, null));
            return clientId;
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IExternalClientRepository>();
                services.AddSingleton<IExternalClientRepository>(_clients);
            });
    }

    /// <summary>Doble en memoria con la misma semántica de bloqueo que el repositorio real.</summary>
    private sealed class InMemoryExternalClients : IExternalClientRepository
    {
        private readonly ConcurrentDictionary<string, ExternalClientCredentials> _byClientId = new(StringComparer.Ordinal);

        public void Put(ExternalClientCredentials client) => _byClientId[client.ClientId] = client;

        public Task<ExternalClientCredentials?> GetCredentialsByClientIdAsync(string clientId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byClientId.TryGetValue(clientId, out var c) ? c : null);

        public Task<DateTimeOffset?> RegisterFailedAttemptAsync(
            Guid id, int maxFailedAttempts, TimeSpan lockDuration, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var client = _byClientId.Values.Single(c => c.Id == id);
            var attempts = client.FailedAttempts + 1;
            var updated = attempts >= maxFailedAttempts
                ? client with { FailedAttempts = 0, LockedUntil = now.Add(lockDuration) }
                : client with { FailedAttempts = attempts };
            Put(updated);
            return Task.FromResult(updated.LockedUntil);
        }

        public Task RegisterTokenIssuedAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var client = _byClientId.Values.Single(c => c.Id == id);
            Put(client with { FailedAttempts = 0, LockedUntil = null });
            return Task.CompletedTask;
        }

        public Task<ExternalClientView> CreateAsync(NewExternalClient client, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ExternalClientView?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ExternalClientView>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ExternalClientView?> UpdateAsync(
            Guid id, ExternalClientChanges changes, Guid? actor, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ExternalClientView?> ReplaceSecretAsync(
            Guid id, string newSecretHash, bool revokePrevious, Guid? actor, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ExternalClientView?> UnlockAsync(Guid id, Guid? actor, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
