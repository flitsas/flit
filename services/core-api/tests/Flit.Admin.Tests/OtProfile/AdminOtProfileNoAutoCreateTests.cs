using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.OtProfile;

/// <summary>
/// Un GET no puede convertir un tenant en organismo de tránsito.
///
/// <c>GET /api/v1/admin/ot/profile</c> CREABA el perfil cuando el tenant no tenía uno, con
/// <c>changedBy: null</c> y la oficina adivinada por el repositorio (primer grant, o un centinela
/// fijo). Bastaba con que alguien abriera el hub del OT para que su tenant quedara declarado
/// organismo, sin decisión ni autor. Así fue como el tenant del SuperAdmin («Empresa Demo FLIT»)
/// acabó siendo el OT de Barranquilla — lo que además lo excluye de Consultas, porque
/// SuperAdminTenantScope descarta a los tenants con perfil OT.
///
/// El alta legítima de un OT ocurre en la consola de organismos (con autor) y en los seeds de dev.
/// </summary>
public sealed class AdminOtProfileNoAutoCreateTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    // Tenant de empresa, SIN perfil de OT — como el del SuperAdmin en QA antes del incidente.
    private readonly Guid _companyTenantId = Guid.NewGuid();
    private readonly Guid _superAdminUserId = Guid.NewGuid();

    public AdminOtProfileNoAutoCreateTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task GetProfile_WithoutOfficeScope_DoesNotCreateTransitOfficeProfile()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("SuperAdmin", _companyTenantId, _superAdminUserId));

        var response = await _client.GetAsync(
            "/api/v1/admin/ot/profile",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "la lectura sigue respondiendo");

        await using var db = CreateDbContext();
        var becameAnOt = await db.TransitOfficeProfiles.AsNoTracking()
            .AnyAsync(p => p.TenantId == _companyTenantId, TestContext.Current.CancellationToken);

        becameAnOt.Should().BeFalse(
            "leer el perfil del OT no puede dar de alta al tenant como organismo de tránsito");
    }

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        db.Tenants.Add(new Tenant
        {
            Id = _companyTenantId,
            Code = $"OT-NOAUTO-{Guid.NewGuid():N}"[..20],
            LegalName = "Empresa sin perfil OT",
            TaxId = TestNit.Unique(),
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Users.Add(new User
        {
            Id = _superAdminUserId,
            Email = $"superadmin-{_superAdminUserId:N}@flit.local",
            DisplayName = "SuperAdmin de prueba",
            Status = "active",
            HomeTenantId = _companyTenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private FlitDbContext CreateDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    private static string MintToken(string role, Guid tenantId, Guid userId)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", userId.ToString()),
                new Claim("role", role),
                new Claim("tenant_id", tenantId.ToString()),
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }

    public void Dispose()
    {
        using var db = CreateDbContext();

        // Si el bug reapareciera, esta limpieza evita dejar el perfil huérfano en la BD de dev.
        db.TransitOfficeProfiles.RemoveRange(
            db.TransitOfficeProfiles.Where(p => p.TenantId == _companyTenantId));
        db.SaveChanges();

        db.Users.RemoveRange(db.Users.Where(u => u.Id == _superAdminUserId));
        db.Tenants.RemoveRange(db.Tenants.Where(t => t.Id == _companyTenantId));
        db.SaveChanges();
    }
}
