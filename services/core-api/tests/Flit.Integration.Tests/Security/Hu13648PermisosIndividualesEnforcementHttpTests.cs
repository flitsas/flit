using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Security;
using Flit.Integration.Tests.MarcaBlanca;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Integration.Tests.Security;

/// <summary>
/// HU #13648 (Feature #13101 F-B, Épica #12753) de punta a punta: login real (AuthUserRepository + RsaJwtTokenIssuer contra
/// Postgres con overrides reales) → token → llamada HTTP al host real (WebApplicationFactory) → 403/200.
/// AC1: deny individual de <c>reportes.read</c> (rol lo tiene) → 403 en <c>/api/v1/analytics/overview</c>
/// (<c>RequireAnyModulePermission</c>, con <c>Security:ModulePermissionEnforcement:Enabled=true</c>).
/// AC2: allow individual de <c>logqx.read</c> (el rol no lo tiene) → 200 en <c>/api/v1/admin/log-qx/tipos</c>
/// (<c>RequirePermission</c> sin policy de rol extra). No se usa <c>improntas.read</c> porque <c>/admin/improntas</c> exige además
/// SuperAdminPolicy y SuperAdmin no admite overrides. AC3 lo cubre <c>PermisosEnforcedArchitectureTests</c> (sin duplicar).
/// </summary>
public sealed class Hu13648PermisosIndividualesEnforcementHttpTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string Email = "it13648@empresa.test";
    private const string OverviewUrl = "/api/v1/analytics/overview?from=2026-06-01&to=2026-06-30";
    private const string LogQxUrl = "/api/v1/admin/log-qx/tipos";

    private static readonly Guid UserId = new("a1364800-0000-4000-8000-000000000001");
    private static readonly Guid RoleId = new("b1364800-0000-4000-8000-000000000001");
    private static readonly Guid ReportesRead = new("c1364800-0000-4000-8000-000000000001");
    private static readonly Guid LogQxRead = new("c1364800-0000-4000-8000-000000000002");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task SeedAsync(bool roleHasReportes)
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        ctx.Users.Add(new User { Id = UserId, Email = Email, DisplayName = Email, Status = "active", HomeTenantId = TenantSeed.LoneId, CreatedAt = DateTimeOffset.UtcNow });
        ctx.UserCredentials.Add(new UserCredential { Id = Guid.CreateVersion7(), UserId = UserId, PasswordHash = "hash", CreatedAt = DateTimeOffset.UtcNow });
        ctx.Roles.Add(new Role
        {
            Id = RoleId, Code = "rol_it13648", Name = "rol_it13648", TargetEntityType = "COMPANY", ProductCode = "tramites",
            IsSystem = false, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        var module = new SecurityModule { Id = Guid.CreateVersion7(), Code = "it13648", Name = "IT 13648", ProductCode = "tramites", CreatedAt = DateTimeOffset.UtcNow };
        ctx.SecurityModules.Add(module);
        ctx.RbacActions.AddRange(NewAction(ReportesRead, module.Id, "reportes.read"), NewAction(LogQxRead, module.Id, "logqx.read"));
        await ctx.SaveChangesAsync(Ct);

        if (roleHasReportes)
            ctx.RoleGrants.Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = RoleId, PermissionId = ReportesRead, CreatedAt = DateTimeOffset.UtcNow });
        ctx.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.CreateVersion7(), TenantId = TenantSeed.LoneId, UserId = UserId, RoleId = RoleId,
            AssignedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);
    }

    private static RbacAction NewAction(Guid id, Guid moduleId, string slug) => new()
    {
        Id = id, ModuleId = moduleId, Slug = slug, Name = slug, HttpMethod = "GET", RoutePattern = "/it/" + slug, CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task OverrideAsync(Guid permission, string effect)
    {
        await using var ctx = NewContext();
        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO security.user_permission_overrides (tenant_id, user_id, product_code, permission_id, effect, granted_by)
            VALUES ({TenantSeed.LoneId}, {UserId}, 'tramites', {permission}, {effect}, {UserId})
            """, Ct);
    }

    /// <summary>Login real: snapshot del usuario desde Postgres y emisión del JWT heredado.</summary>
    private async Task<string> LoginAsync()
    {
        await using var db = NewContext();
        var user = (await new AuthUserRepository(db, new UserRoleAssignmentRepository(db)).FindByEmailAsync(Email, Ct))!;
        var issuer = new RsaJwtTokenIssuer(
            new JwtKeyMaterial { SigningKey = new RsaSecurityKey(RSA.Create(2048)), Issuer = "https://api.flit.co", Audience = "flit-api" },
            Options.Create(new JwtSettings()));
        return issuer.IssueToken(
            user.UserId, user.Email, user.TenantId, user.TenantName, user.TenantTaxId, user.EntityType, user.TenantType,
            user.IsGroupParent, user.ActiveRoles, user.PermissionSlugs, "flit").Token;
    }

    private async Task<HttpStatusCode> CallAsync(string url)
    {
        var token = await LoginAsync();
        await using var factory = new MarcaBlancaApiFactory(Fixture);
        await using var configured = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Security:ModulePermissionEnforcement:Enabled"] = "true" })));
        using var client = configured.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync(url, Ct);
        return response.StatusCode;
    }

    [PostgresFact]
    public async Task AC1_DenyIndividual_BloqueaUnaRutaQueElRolPermite_403()
    {
        await SeedAsync(roleHasReportes: true);
        (await CallAsync(OverviewUrl)).Should().NotBe(HttpStatusCode.Forbidden, "sin override el rol sí tiene reportes.read");

        await OverrideAsync(ReportesRead, "deny");

        (await CallAsync(OverviewUrl)).Should().Be(HttpStatusCode.Forbidden);
    }

    [PostgresFact]
    public async Task AC2_AllowIndividual_HabilitaUnaRutaQueElRolNoTiene_200()
    {
        await SeedAsync(roleHasReportes: false);
        (await CallAsync(LogQxUrl)).Should().Be(HttpStatusCode.Forbidden, "sin override el rol no tiene logqx.read");

        await OverrideAsync(LogQxRead, "allow");

        (await CallAsync(LogQxUrl)).Should().Be(HttpStatusCode.OK);
    }
}
