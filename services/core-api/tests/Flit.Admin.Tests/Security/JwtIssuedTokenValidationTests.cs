using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Security;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Security;

/// <summary>
/// HU #12896 (A-03) — con <c>Jwt:PersistSigningKey</c> y <c>Jwt:ValidateIssuedTokens</c> encendidas (como las deja
/// el compose de los servidores), la API solo acepta los tokens que ella misma firmó, vigentes y con su emisor y
/// audiencia. Antes aceptaba cualquier token sin llave pública configurada (a-inventario-ambientes.md, punto 4).
/// </summary>
public sealed class JwtIssuedTokenValidationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string Password = "ValidacionPass1!";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private string Email => $"jwt-{_suffix}@flit.local";

    public JwtIssuedTokenValidationTests(WebApplicationFactory<Program> factory)
    {
        _factory = Validating(factory);
        SeedAsync().GetAwaiter().GetResult();
    }

    private static WebApplicationFactory<Program> Validating(WebApplicationFactory<Program> factory) =>
        factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Jwt:PersistSigningKey", "true");
            b.UseSetting("Jwt:ValidateIssuedTokens", "true");
        });

    [Fact]
    public async Task TokenDelLogin_EsAceptado()
    {
        var token = await LoginAsync(_factory);

        (await MeAsync(_factory, token)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TokenFabricadoSinFirmaValida_EsRechazado()
    {
        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity([new Claim("sub", _userId.ToString()), new Claim("role", "SuperAdmin"), new Claim("tenant_id", _tenantId.ToString())]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))), SecurityAlgorithms.HmacSha256),
        });

        (await MeAsync(_factory, forged)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenFirmadoConOtraLlaveRsa_EsRechazado()
    {
        using var otherKey = RSA.Create(2048);
        var token = Sign(new RsaSecurityKey(otherKey), "https://api.flit.co", "flit-api", DateTime.UtcNow.AddHours(1));

        (await MeAsync(_factory, token)).Should().Be(HttpStatusCode.Unauthorized);
        // El frontend reconoce SESSION_EXPIRED y lleva al login: es lo que verá quien tenga una sesión
        // firmada con la llave efímera anterior al desplegar.
        (await MeCodeAsync(_factory, token)).Should().Be("SESSION_EXPIRED");
    }

    [Fact]
    public async Task TokenVencido_EsRechazado()
    {
        var key = _factory.Services.GetRequiredService<JwtKeyMaterial>();
        var token = Sign(key.SigningKey, key.Issuer, key.Audience, DateTime.UtcNow.AddMinutes(-5), issuedAt: DateTime.UtcNow.AddHours(-1));

        (await MeAsync(_factory, token)).Should().Be(HttpStatusCode.Unauthorized);
        (await MeCodeAsync(_factory, token)).Should().Be("SESSION_EXPIRED");
    }

    [Fact]
    public async Task TokenConOtraAudiencia_EsRechazado()
    {
        var key = _factory.Services.GetRequiredService<JwtKeyMaterial>();
        var token = Sign(key.SigningKey, key.Issuer, "otro-servicio", DateTime.UtcNow.AddHours(1));

        (await MeAsync(_factory, token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LaLlaveSobreviveAUnReinicio_OtraInstanciaAceptaElToken()
    {
        var token = await LoginAsync(_factory);

        // Una instancia nueva de la API (otro host, otro contenedor de DI) simula un reinicio o despliegue.
        using var restarted = Validating(new WebApplicationFactory<Program>());

        (await MeAsync(restarted, token)).Should().Be(HttpStatusCode.OK);
    }

    private string Sign(SecurityKey key, string issuer, string audience, DateTime expires, DateTime? issuedAt = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", _userId.ToString()), new Claim("tenant_id", _tenantId.ToString())]),
            IssuedAt = issuedAt ?? DateTime.UtcNow,
            NotBefore = issuedAt ?? DateTime.UtcNow,
            Expires = expires,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        });

    private async Task<string> LoginAsync(WebApplicationFactory<Program> factory)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<HttpStatusCode> MeAsync(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken)).StatusCode;
    }

    private static async Task<string?> MeCodeAsync(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var body = await (await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        if (string.IsNullOrWhiteSpace(body)) return null;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Code = $"IT-JWT-{_suffix}", LegalName = "Empresa validación JWT", TaxId = TestNit.Unique(),
            TenantType = "RENTING", IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        db.Users.Add(new User { Id = _userId, Email = Email, DisplayName = "Validación JWT", Status = "active", HomeTenantId = _tenantId, CreatedAt = DateTimeOffset.UtcNow });
        db.UserCredentials.Add(new UserCredential { Id = Guid.CreateVersion7(), UserId = _userId, PasswordHash = hasher.Hash(Password), CreatedAt = DateTimeOffset.UtcNow });
        db.Roles.Add(new Role { Id = _roleId, Code = $"JwtRadicador-{_suffix}", Name = "Radicador JWT", TargetEntityType = "COMPANY", ProductCode = "tramites", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        db.UserRoleAssignments.Add(new UserRoleAssignment { Id = Guid.CreateVersion7(), TenantId = _tenantId, UserId = _userId, RoleId = _roleId, AssignedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    public void Dispose()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        db.UserRoleAssignments.Where(a => a.UserId == _userId).ExecuteDelete();
        db.Roles.Where(r => r.Id == _roleId).ExecuteDelete();
        db.UserCredentials.Where(c => c.UserId == _userId).ExecuteDelete();
        db.Set<TenantProductEntity>().Where(r => r.TenantId == _tenantId).ExecuteDelete();
        db.TenantConfigAuditLogs.Where(a => a.TenantId == _tenantId).ExecuteDelete();
        db.Users.Where(u => u.Id == _userId).ExecuteDelete();
        db.Tenants.Where(t => t.Id == _tenantId).ExecuteDelete();
    }
}
