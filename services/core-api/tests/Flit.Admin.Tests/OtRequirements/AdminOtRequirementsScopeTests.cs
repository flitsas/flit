using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.OtRequirements;

/// <summary>
/// Regresión del scope de <c>/api/v1/admin/ot/requirements</c>.
///
/// El SuperAdmin navegando el hub de un organismo leía y escribía SIEMPRE los requisitos del OT
/// de SU PROPIO tenant, ignorando el <c>transitOfficeId</c> que el frontend ya mandaba: el GET lo
/// recibía pero el handler lo descartaba, y el PUT ni siquiera declaraba el parámetro. La pantalla
/// mostraba el nombre de un organismo con los datos de otro, y guardar pisaba al otro.
///
/// Estos tests fallan contra el código anterior al fix (mismo patrón de integración real que
/// <c>AdminOtUsersEndpointsTests</c>: WebApplicationFactory + FlitDbContext, con GUIDs aleatorios
/// por ejecución y limpieza al final).
/// </summary>
public sealed class AdminOtRequirementsScopeTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    // Dos OT completos y distintos: el que se pide (A) y el vecino que NO se debe tocar (B).
    private readonly Guid _officeAId = Guid.NewGuid();
    private readonly Guid _tenantAId = Guid.NewGuid();
    private readonly Guid _officeBId = Guid.NewGuid();
    private readonly Guid _tenantBId = Guid.NewGuid();

    // El SuperAdmin vive en un tenant propio SIN perfil de OT — como el de producción/QA.
    private readonly Guid _superAdminTenantId = Guid.NewGuid();
    private readonly Guid _superAdminUserId = Guid.NewGuid();

    public AdminOtRequirementsScopeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedAsync().GetAwaiter().GetResult();
    }

    [Fact] // El GET devuelve los requisitos del organismo PEDIDO, no los del tenant del token.
    public async Task GetRequirements_AsSuperAdmin_ReturnsRequirementsOfRequestedOffice()
    {
        Authenticate();

        var response = await _client.GetAsync(
            $"/api/v1/admin/ot/requirements?transitOfficeId={_officeAId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<RequirementsDto>(
            TestContext.Current.CancellationToken);

        body.Should().NotBeNull();
        body!.TransitOfficeId.Should().Be(_officeAId, "se pidió el organismo A");
        body.AllowPlatePreassign.Should().BeFalse("A tiene la ruta de placa apagada");
        body.RequiresRnmc.Should().BeTrue("A exige RNMC; B no — así se distingue una fila de la otra");
    }

    [Fact] // El PUT escribe en el organismo pedido y NO toca al vecino.
    public async Task PutRequirements_AsSuperAdmin_WritesRequestedOfficeAndLeavesOtherUntouched()
    {
        Authenticate();

        var response = await _client.PutAsJsonAsync(
            $"/api/v1/admin/ot/requirements?transitOfficeId={_officeAId}",
            new { allowPlatePreassign = true },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();

        var a = await db.OtRequirements.AsNoTracking()
            .SingleAsync(r => r.TransitOfficeId == _officeAId, TestContext.Current.CancellationToken);
        a.AllowPlatePreassign.Should().BeTrue("es el organismo que se pidió configurar");
        a.TenantId.Should().Be(_tenantAId, "la fila debe pertenecer al tenant OT dueño, no al del SuperAdmin");
        a.RequiresRnmc.Should().BeTrue("el PUT parcial no debe alterar los flags no enviados");

        var b = await db.OtRequirements.AsNoTracking()
            .SingleAsync(r => r.TransitOfficeId == _officeBId, TestContext.Current.CancellationToken);
        b.AllowPlatePreassign.Should().BeTrue("B ya estaba en true y nadie lo tocó");
        b.RequiresRnmc.Should().BeFalse("B no debe haber cambiado en absoluto");
    }

    [Fact] // Un organismo sin tenant OT aprovisionado no se configura a ciegas: 404, no un pisotón.
    public async Task PutRequirements_ForOfficeWithoutOtTenant_Returns404()
    {
        Authenticate();

        var orphanOfficeId = Guid.NewGuid();
        await using (var db = CreateDbContext())
        {
            db.TransitOffices.Add(new TransitOffice
            {
                Id = orphanOfficeId,
                Code = $"O{Guid.NewGuid():N}"[..10],
                Name = "OT sin tenant",
                DepartmentCode = "99",
                CityCode = "99999",
                IsActive = true,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        try
        {
            var response = await _client.PutAsJsonAsync(
                $"/api/v1/admin/ot/requirements?transitOfficeId={orphanOfficeId}",
                new { allowPlatePreassign = true },
                TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await using var db = CreateDbContext();
            db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => o.Id == orphanOfficeId));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    private void Authenticate() =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("SuperAdmin", _superAdminTenantId, _superAdminUserId));

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        db.TransitOffices.AddRange(
            NewOffice(_officeAId, "OT requirements scope A"),
            NewOffice(_officeBId, "OT requirements scope B"));

        db.Tenants.AddRange(
            NewTenant(_tenantAId, "OT Requirements Scope A"),
            NewTenant(_tenantBId, "OT Requirements Scope B"),
            NewTenant(_superAdminTenantId, "Empresa del SuperAdmin (sin perfil OT)"));

        db.Users.Add(new User
        {
            Id = _superAdminUserId,
            Email = $"superadmin-{_superAdminUserId:N}@flit.local",
            DisplayName = "SuperAdmin de prueba",
            Status = "active",
            HomeTenantId = _superAdminTenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Perfiles OT: cada tenant OT es dueño de su oficina. El tenant del SuperAdmin NO lleva
        // perfil a propósito — es lo que lo hace un tenant de empresa normal.
        db.TransitOfficeProfiles.AddRange(
            NewProfile(_tenantAId, _officeAId),
            NewProfile(_tenantBId, _officeBId));

        // Filas distinguibles entre sí: si el endpoint devuelve/escribe la que no es, se nota.
        db.OtRequirements.AddRange(
            new OtRequirementsEntity
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantAId,
                TransitOfficeId = _officeAId,
                RequiresRnmc = true,
                AllowPlatePreassign = false,
                IdentityValidationEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new OtRequirementsEntity
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantBId,
                TransitOfficeId = _officeBId,
                RequiresRnmc = false,
                AllowPlatePreassign = true,
                IdentityValidationEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static TransitOffice NewOffice(Guid id, string name) => new()
    {
        Id = id,
        Code = $"R{Guid.NewGuid():N}"[..10],
        Name = name,
        DepartmentCode = "99",
        CityCode = "99999",
        IsActive = true,
    };

    private static Tenant NewTenant(Guid id, string legalName) => new()
    {
        Id = id,
        Code = $"OT-REQ-{Guid.NewGuid():N}"[..20],
        LegalName = legalName,
        TaxId = TestNit.Unique(),
        TenantType = "RENTING",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static TransitOfficeProfile NewProfile(Guid tenantId, Guid officeId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        TransitOfficeId = officeId,
        OperationMode = "dashboard",
        QuipuxReadOnly = false,
        CreatedAt = DateTimeOffset.UtcNow,
    };

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

    private sealed record RequirementsDto(
        Guid TransitOfficeId,
        bool RequiresRnmc,
        bool AllowPlatePreassign,
        bool IdentityValidationEnabled);

    public void Dispose()
    {
        using var db = CreateDbContext();

        db.OtRequirements.RemoveRange(db.OtRequirements.Where(r =>
            r.TenantId == _tenantAId || r.TenantId == _tenantBId || r.TenantId == _superAdminTenantId));
        db.TransitOfficeProfiles.RemoveRange(db.TransitOfficeProfiles.Where(p =>
            p.TenantId == _tenantAId || p.TenantId == _tenantBId || p.TenantId == _superAdminTenantId));
        db.SaveChanges();

        db.Users.RemoveRange(db.Users.Where(u => u.Id == _superAdminUserId));
        db.Tenants.RemoveRange(db.Tenants.Where(t =>
            t.Id == _tenantAId || t.Id == _tenantBId || t.Id == _superAdminTenantId));
        db.TransitOffices.RemoveRange(db.TransitOffices.Where(o =>
            o.Id == _officeAId || o.Id == _officeBId));
        db.SaveChanges();
    }
}
