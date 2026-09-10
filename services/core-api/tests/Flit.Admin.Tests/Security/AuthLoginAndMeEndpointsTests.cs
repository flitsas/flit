using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Flit.Admin.Tests.Security;

/// <summary>
/// HU #10616 — enriquecer el JWT (empresa/NIT/tipo de entidad) y corregir <c>/me</c> para que
/// exponga los roles activos y la unión completa de sus permisos. Cubre los 5 AC contra una
/// BD real (WebApplicationFactory + FlitDbContext real, mismo patrón que
/// <see cref="UserRoleAssignmentEndpointsTests"/>), ejerciendo el flujo HTTP real de
/// <c>POST /api/v1/auth/login</c> + <c>GET /api/v1/auth/me</c> de punta a punta.
/// </summary>
public sealed class AuthLoginAndMeEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string Password = "DemoPass1!";

    // NIT único por ejecución: identity.tenants tiene índice único parcial sobre tax_id y la base
    // de pruebas es compartida. El valor se afirma contra estos mismos campos, no contra literales.
    private static readonly string CompanyNit = TestNit.Unique();
    private static readonly string TransitOfficeNit = TestNit.Unique();

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _companyTenantId = Guid.NewGuid();
    private readonly Guid _otTenantId = Guid.NewGuid();
    private readonly Guid _noNitTenantId = Guid.NewGuid();
    private readonly Guid _transitOfficeId = Guid.NewGuid();

    private readonly Guid _companyUserId = Guid.NewGuid();
    private readonly Guid _otUserId = Guid.NewGuid();
    private readonly Guid _noNitUserId = Guid.NewGuid();
    private readonly Guid _multiRoleUserId = Guid.NewGuid();

    private Guid _companyRoleId;
    private Guid _otRoleId;
    private Guid _noNitRoleId;
    private Guid _multiRoleAId;

    public AuthLoginAndMeEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        SeedAsync().GetAwaiter().GetResult();
    }

    // AC1 — login exitoso de un tenant COMPANY con NIT: el JWT trae company_name/company_nit/
    // entity_type=COMPANY, y /me expone esos mismos datos.
    [Fact]
    public async Task Login_CompanyTenantWithNit_JwtAndMeExposeCompanyDataAndEntityType()
    {
        var accessToken = await LoginAsync($"company-{_companyUserId:N}@flit.local");

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken);
        jwt.GetClaim("company_name").Value.Should().Be("Acme Renting HU10616");
        jwt.GetClaim("company_nit").Value.Should().Be(CompanyNit);
        jwt.GetClaim("entity_type").Value.Should().Be("COMPANY");

        var me = await GetMeAsync(accessToken);
        me.Should().NotBeNull();
        me!.CompanyName.Should().Be("Acme Renting HU10616");
        me.CompanyNit.Should().Be(CompanyNit);
        me.EntityType.Should().Be("COMPANY");
        me.Roles.Should().ContainSingle(r => r.RoleId == _companyRoleId);
    }

    // AC2 — login exitoso de un tenant Organismo de Tránsito: entity_type=TRANSIT_OFFICE.
    [Fact]
    public async Task Login_TransitOfficeTenant_JwtEntityTypeIsTransitOffice()
    {
        var accessToken = await LoginAsync($"ot-{_otUserId:N}@flit.local");

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken);
        jwt.GetClaim("entity_type").Value.Should().Be("TRANSIT_OFFICE");
        jwt.GetClaim("company_name").Value.Should().Be("Organismo de Tránsito HU10616");
        jwt.GetClaim("company_nit").Value.Should().Be(TransitOfficeNit);

        var me = await GetMeAsync(accessToken);
        me!.EntityType.Should().Be("TRANSIT_OFFICE");
        me.CompanyNit.Should().Be(TransitOfficeNit);
    }

    // AC3 — /me devuelve los roles ACTIVOS del usuario. Con el rol único es exactamente uno; la
    // forma de lista se conserva en el contrato (antes la HU #10506 permitía varios).
    [Fact]
    public async Task Login_MeExposesTheActiveRole()
    {
        var accessToken = await LoginAsync($"multi-{_multiRoleUserId:N}@flit.local");

        var me = await GetMeAsync(accessToken);
        me!.Roles.Should().HaveCount(1);
        me.Roles.Select(r => r.RoleId).Should().BeEquivalentTo([_multiRoleAId]);
    }

    // AC4 — tenant sin NIT registrado: el login se completa sin error y company_nit va vacío.
    [Fact]
    public async Task Login_TenantWithoutTaxId_CompletesLoginAndEmitsEmptyNit()
    {
        var accessToken = await LoginAsync($"nonit-{_noNitUserId:N}@flit.local");

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken);
        jwt.GetClaim("company_nit").Value.Should().BeEmpty();

        var me = await GetMeAsync(accessToken);
        me!.CompanyNit.Should().BeEmpty();
    }

    // AC5 — /me sin token o con token inválido: 401, sin exponer roles/permisos/empresa.
    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithMalformedToken_Returns401()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-valid-jwt");

        var response = await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> LoginAsync(string email)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = Password },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>(TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        return body!.AccessToken;
    }

    private async Task<CurrentUserResponseDto?> GetMeAsync(string accessToken)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<CurrentUserResponseDto>(TestContext.Current.CancellationToken);
    }

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var passwordHash = hasher.Hash(Password);

        db.Tenants.Add(new Tenant
        {
            Id = _companyTenantId,
            Code = $"CO-HU10616-{Guid.NewGuid():N}"[..20],
            LegalName = "Acme Renting HU10616",
            TaxId = CompanyNit,
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Tenants.Add(new Tenant
        {
            Id = _otTenantId,
            Code = $"OT-HU10616-{Guid.NewGuid():N}"[..20],
            LegalName = "Organismo de Tránsito HU10616",
            TaxId = TransitOfficeNit,
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Tenants.Add(new Tenant
        {
            Id = _noNitTenantId,
            Code = $"CO-NONIT-{Guid.NewGuid():N}"[..20],
            LegalName = "Tenant Legacy Sin NIT HU10616",
            TaxId = string.Empty,
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.TransitOffices.Add(new TransitOffice
        {
            Id = _transitOfficeId,
            Code = $"T{Guid.NewGuid():N}"[..10],
            Name = "OT HU10616 tests",
            DepartmentCode = "99",
            CityCode = "99999",
            IsActive = true,
        });

        db.Users.AddRange(
            new User
            {
                Id = _companyUserId,
                Email = $"company-{_companyUserId:N}@flit.local",
                DisplayName = "Usuario Compañía HU10616",
                Status = "active",
                HomeTenantId = _companyTenantId,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new User
            {
                Id = _otUserId,
                Email = $"ot-{_otUserId:N}@flit.local",
                DisplayName = "Usuario OT HU10616",
                Status = "active",
                HomeTenantId = _otTenantId,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new User
            {
                Id = _noNitUserId,
                Email = $"nonit-{_noNitUserId:N}@flit.local",
                DisplayName = "Usuario Sin NIT HU10616",
                Status = "active",
                HomeTenantId = _noNitTenantId,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new User
            {
                Id = _multiRoleUserId,
                Email = $"multi-{_multiRoleUserId:N}@flit.local",
                DisplayName = "Usuario Multi-rol HU10616",
                Status = "active",
                HomeTenantId = _companyTenantId,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.UserCredentials.AddRange(
            new UserCredential { Id = Guid.NewGuid(), UserId = _companyUserId, PasswordHash = passwordHash, CreatedAt = DateTimeOffset.UtcNow },
            new UserCredential { Id = Guid.NewGuid(), UserId = _otUserId, PasswordHash = passwordHash, CreatedAt = DateTimeOffset.UtcNow },
            new UserCredential { Id = Guid.NewGuid(), UserId = _noNitUserId, PasswordHash = passwordHash, CreatedAt = DateTimeOffset.UtcNow },
            new UserCredential { Id = Guid.NewGuid(), UserId = _multiRoleUserId, PasswordHash = passwordHash, CreatedAt = DateTimeOffset.UtcNow });

        db.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = _otTenantId,
            TransitOfficeId = _transitOfficeId,
            OperationMode = "dashboard",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var companyRole = new Role
        {
            Id = Guid.NewGuid(),
            Code = $"Hu10616Company-{Guid.NewGuid():N}"[..40],
            Name = "HU10616 Company Role",
            TargetEntityType = "COMPANY",
            IsSystem = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var otRole = new Role
        {
            Id = Guid.NewGuid(),
            Code = $"Hu10616Ot-{Guid.NewGuid():N}"[..40],
            Name = "HU10616 OT Role",
            TargetEntityType = "TRANSIT_OFFICE",
            IsSystem = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var noNitRole = new Role
        {
            Id = Guid.NewGuid(),
            Code = $"Hu10616NoNit-{Guid.NewGuid():N}"[..40],
            Name = "HU10616 No-Nit Role",
            TargetEntityType = "COMPANY",
            IsSystem = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var multiRoleA = new Role
        {
            Id = Guid.NewGuid(),
            Code = $"Hu10616MultiA-{Guid.NewGuid():N}"[..40],
            Name = "HU10616 Multi Role A",
            TargetEntityType = "COMPANY",
            IsSystem = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Roles.AddRange(companyRole, otRole, noNitRole, multiRoleA);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _companyRoleId = companyRole.Id;
        _otRoleId = otRole.Id;
        _noNitRoleId = noNitRole.Id;
        _multiRoleAId = multiRoleA.Id;

        db.UserRoleAssignments.AddRange(
            new UserRoleAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = _companyTenantId,
                UserId = _companyUserId,
                RoleId = companyRole.Id,
                AssignedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new UserRoleAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = _otTenantId,
                UserId = _otUserId,
                RoleId = otRole.Id,
                AssignedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new UserRoleAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = _noNitTenantId,
                UserId = _noNitUserId,
                RoleId = noNitRole.Id,
                AssignedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new UserRoleAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = _companyTenantId,
                UserId = _multiRoleUserId,
                RoleId = multiRoleA.Id,
                AssignedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private FlitDbContext CreateDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    public void Dispose()
    {
        using var db = CreateDbContext();

        TryDelete(() => db.UserRoleAssignments
            .Where(a => a.TenantId == _companyTenantId || a.TenantId == _otTenantId || a.TenantId == _noNitTenantId)
            .ExecuteDelete());
        TryDelete(() => db.TransitOfficeProfiles.Where(p => p.TenantId == _otTenantId).ExecuteDelete());
        TryDelete(() => db.UserCredentials
            .Where(c => c.UserId == _companyUserId || c.UserId == _otUserId
                        || c.UserId == _noNitUserId || c.UserId == _multiRoleUserId)
            .ExecuteDelete());

        // El login audita cada intento con changed_by = usuario (HU #10678); esas filas deben
        // eliminarse antes que los usuarios o el FK tenant_config_audit_logs_changed_by_fkey
        // bloquea el borrado. Va en la MISMA etapa que el borrado del usuario: la traza se
        // escribe en su propio scope, así que una fila puede confirmarse entre ambos DELETE.
        TryDelete(() =>
        {
            db.TenantConfigAuditLogs
                .Where(l => l.ChangedBy == _companyUserId || l.ChangedBy == _otUserId
                            || l.ChangedBy == _noNitUserId || l.ChangedBy == _multiRoleUserId
                            || l.TargetEntityId == _companyUserId || l.TargetEntityId == _otUserId
                            || l.TargetEntityId == _noNitUserId || l.TargetEntityId == _multiRoleUserId)
                .ExecuteDelete();
            db.Users
                .Where(u => u.Id == _companyUserId || u.Id == _otUserId
                            || u.Id == _noNitUserId || u.Id == _multiRoleUserId)
                .ExecuteDelete();
        });

        TryDelete(() => db.Roles
            .Where(r => r.Id == _companyRoleId || r.Id == _otRoleId
                        || r.Id == _noNitRoleId || r.Id == _multiRoleAId)
            .ExecuteDelete());

        TryDelete(() => db.Tenants
            .Where(t => t.Id == _companyTenantId || t.Id == _otTenantId || t.Id == _noNitTenantId)
            .ExecuteDelete());
        TryDelete(() => db.TransitOffices.Where(o => o.Id == _transitOfficeId).ExecuteDelete());
    }

    /// <summary>
    /// Etapa de limpieza aislada: la BD de desarrollo es COMPARTIDA, así que el fallo de una
    /// etapa no debe impedir las siguientes ni hacer fallar el test (esto es housekeeping, el
    /// test ya pasó). Se reintenta una vez por si una escritura concurrente vuelve a bloquear.
    /// </summary>
    private static void TryDelete(Action stage)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                stage();
                return;
            }
            catch (DbUpdateException)
            {
                // Reintento.
            }
            catch (DbException)
            {
                // ExecuteDelete no envuelve el error del proveedor (p. ej. violación de FK).
            }
        }
    }

    private sealed record LoginResponseDto(string AccessToken, int ExpiresInSeconds, string TokenType);

    private sealed record CurrentUserRoleDto(Guid RoleId, string RoleCode);

    private sealed record CurrentUserResponseDto(
        Guid UserId,
        string Email,
        Guid TenantId,
        IReadOnlyList<CurrentUserRoleDto> Roles,
        IReadOnlyList<string> Permissions,
        string CompanyName,
        string CompanyNit,
        string EntityType);
}
