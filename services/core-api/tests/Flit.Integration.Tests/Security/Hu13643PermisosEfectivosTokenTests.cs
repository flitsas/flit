using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Api.Identity;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Persistence.Repositories.Platform;
using Flit.Infrastructure.Security;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Platform.Application.Access;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Integration.Tests.Security;

/// <summary>
/// HU #13643 (Feature #13101 F-B, Épica #12753) contra PostgreSQL real: el claim <c>permissions</c> del token es
/// rol + allow - deny del tenant, con los allow filtrados por productos habilitados, y es el mismo por el JWT heredado
/// (<c>AuthUserRepository</c> + <c>RsaJwtTokenIssuer</c>) y por OIDC (<c>OidcPrincipalFactory</c>). AC1 a AC4.
/// </summary>
public sealed class Hu13643PermisosEfectivosTokenTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string Email = "it13643@empresa.test";

    private static readonly Guid UserId = new("a1364300-0000-4000-8000-000000000001");
    private static readonly Guid AdminCompanyId = new("b1364300-0000-4000-8000-000000000001");
    private static readonly Guid AdminTramitesId = new("b1364300-0000-4000-8000-000000000002");
    private static readonly Guid SuperAdminId = new("b1364300-0000-4000-8000-000000000003");
    private static readonly Guid PlatPermission = new("c1364300-0000-4000-8000-000000000001");
    private static readonly Guid TramitesRead = new("c1364300-0000-4000-8000-000000000002");
    private static readonly Guid ReportesRead = new("c1364300-0000-4000-8000-000000000003");
    private static readonly Guid PlatExtra = new("c1364300-0000-4000-8000-000000000004");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Asignar AdminCompany espeja admin_tramites (HU #12964). AdminCompany (plataforma) tiene it.plat.manage y admin_tramites tiene it.tramites.read; it.reportes.read (tramites)
    // e it.plat.extra (plataforma) existen en el catalogo pero ningun rol los tiene.
    private async Task SeedAsync(params Guid[] roles)
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        ctx.Users.Add(new User { Id = UserId, Email = Email, DisplayName = Email, Status = "active", HomeTenantId = TenantSeed.LoneId, CreatedAt = DateTimeOffset.UtcNow });
        ctx.UserCredentials.Add(new UserCredential { Id = Guid.CreateVersion7(), UserId = UserId, PasswordHash = "hash", CreatedAt = DateTimeOffset.UtcNow });
        ctx.Roles.AddRange(
            NewRole(AdminCompanyId, "AdminCompany", "plataforma"),
            NewRole(AdminTramitesId, "admin_tramites", "tramites"),
            NewRole(SuperAdminId, "SuperAdmin", "plataforma"));
        var plat = new SecurityModule { Id = Guid.CreateVersion7(), Code = "it43-plat", Name = "Plat", ProductCode = "plataforma", CreatedAt = DateTimeOffset.UtcNow };
        var tram = new SecurityModule { Id = Guid.CreateVersion7(), Code = "it43-tram", Name = "Tram", ProductCode = "tramites", CreatedAt = DateTimeOffset.UtcNow };
        ctx.SecurityModules.AddRange(plat, tram);
        ctx.RbacActions.AddRange(
            NewAction(PlatPermission, plat.Id, "it.plat.manage"),
            NewAction(PlatExtra, plat.Id, "it.plat.extra"),
            NewAction(TramitesRead, tram.Id, "it.tramites.read"),
            NewAction(ReportesRead, tram.Id, "it.reportes.read"));
        await ctx.SaveChangesAsync(Ct);

        ctx.RoleGrants.AddRange(
            new RoleGrant { Id = Guid.CreateVersion7(), RoleId = AdminCompanyId, PermissionId = PlatPermission, CreatedAt = DateTimeOffset.UtcNow },
            new RoleGrant { Id = Guid.CreateVersion7(), RoleId = AdminTramitesId, PermissionId = TramitesRead, CreatedAt = DateTimeOffset.UtcNow });
        foreach (var role in roles)
        {
            ctx.UserRoleAssignments.Add(new UserRoleAssignment
            {
                Id = Guid.CreateVersion7(), TenantId = TenantSeed.LoneId, UserId = UserId, RoleId = role,
                AssignedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await ctx.SaveChangesAsync(Ct);
    }

    private static Role NewRole(Guid id, string code, string product) => new()
    {
        Id = id, Code = code, Name = code, TargetEntityType = "COMPANY", ProductCode = product, IsSystem = true, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
    };

    private static RbacAction NewAction(Guid id, Guid moduleId, string slug) => new()
    {
        Id = id, ModuleId = moduleId, Slug = slug, Name = slug, HttpMethod = "GET", RoutePattern = "/it/" + slug, CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task OverrideAsync(Guid permission, string product, string effect, Guid? tenant = null, bool deleted = false)
    {
        await using var ctx = NewContext();
        DateTimeOffset? deletedAt = deleted ? DateTimeOffset.UtcNow : null;
        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO security.user_permission_overrides (tenant_id, user_id, product_code, permission_id, effect, granted_by, deleted_at)
            VALUES ({tenant ?? TenantSeed.LoneId}, {UserId}, {product}, {permission}, {effect}, {UserId}, {deletedAt})
            """, Ct);
    }

    private async Task DisableProductAsync(string product)
    {
        await using var ctx = NewContext();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE platform.tenant_products SET enabled = false WHERE tenant_id = {TenantSeed.LoneId} AND product_code = {product}", Ct);
    }

    /// <summary>Emision heredada completa: snapshot real + RsaJwtTokenIssuer; devuelve el claim permissions.</summary>
    private async Task<string[]> LegacyClaimAsync()
    {
        await using var db = NewContext();
        var user = (await new AuthUserRepository(db, new UserRoleAssignmentRepository(db)).FindByEmailAsync(Email, Ct))!;
        var issuer = new RsaJwtTokenIssuer(
            new JwtKeyMaterial { SigningKey = new RsaSecurityKey(RSA.Create(2048)), Issuer = "https://api.flit.co", Audience = "flit-api" },
            Options.Create(new JwtSettings()));
        var issued = issuer.IssueToken(
            user.UserId, user.Email, user.TenantId, user.TenantName, user.TenantTaxId, user.EntityType, user.TenantType,
            user.IsGroupParent, user.ActiveRoles, user.PermissionSlugs, "flit");
        return new JwtSecurityTokenHandler().ReadJwtToken(issued.Token).Claims.Where(c => c.Type == "permissions").Select(c => c.Value).ToArray();
    }

    private async Task<string[]> OidcClaimAsync(string product)
    {
        await using var db = NewContext();
        var factory = new OidcPrincipalFactory(
            new AuthUserRepository(db, new UserRoleAssignmentRepository(db)),
            new ProductAccessResolver(new ProductAccessStore(db)));
        var source = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId.ToString()), new Claim("email", Email)]));
        var grant = await factory.ForUserAsync(source, product, ["openid"], Ct);
        grant.DeniedCode.Should().BeNull();
        return JsonSerializer.Deserialize<string[]>(grant.Principal!.FindFirst("permissions")!.Value)!;
    }

    [PostgresFact]
    public async Task AC1_SinPermisosIndividuales_ElClaimEsElDelRol()
    {
        await SeedAsync(AdminCompanyId);

        (await LegacyClaimAsync()).Should().BeEquivalentTo("it.plat.manage", "it.tramites.read");
    }

    [PostgresFact]
    public async Task AC2_AllowYDeny_SumanYRestanAlRol()
    {
        await SeedAsync(AdminCompanyId);
        await OverrideAsync(ReportesRead, "tramites", "allow");
        await OverrideAsync(TramitesRead, "tramites", "deny");

        (await LegacyClaimAsync()).Should().BeEquivalentTo("it.plat.manage", "it.reportes.read");
    }

    [PostgresFact]
    public async Task AC3_AllowDeUnProductoNoHabilitado_NoApareceEnElClaim()
    {
        await SeedAsync(AdminCompanyId);
        await OverrideAsync(ReportesRead, "tramites", "allow");
        await OverrideAsync(PlatExtra, "plataforma", "allow");
        await DisableProductAsync("tramites");

        (await LegacyClaimAsync()).Should().Contain("it.plat.extra").And.NotContain("it.reportes.read");
    }

    [PostgresFact]
    public async Task SoloOverridesVigentesDelTenantDelToken()
    {
        await SeedAsync(AdminCompanyId);
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.GroupParent());
            await ctx.SaveChangesAsync(Ct);
        }

        await OverrideAsync(PlatExtra, "plataforma", "allow", deleted: true);
        await OverrideAsync(PlatPermission, "plataforma", "deny", tenant: TenantSeed.ParentId);

        (await LegacyClaimAsync()).Should().BeEquivalentTo("it.plat.manage", "it.tramites.read");
    }

    [PostgresFact]
    public async Task SuperAdmin_NoAdmiteOverrides()
    {
        await SeedAsync(SuperAdminId);
        await OverrideAsync(PlatExtra, "plataforma", "allow");

        (await LegacyClaimAsync()).Should().NotContain("it.plat.extra");
    }

    [PostgresFact]
    public async Task AC4_ParidadLegacyYOidc_MismoUsuarioMismaConfiguracion()
    {
        await SeedAsync(AdminCompanyId);
        await OverrideAsync(ReportesRead, "tramites", "allow");
        await OverrideAsync(TramitesRead, "tramites", "deny");
        await OverrideAsync(PlatExtra, "plataforma", "allow");

        var legacy = await LegacyClaimAsync();
        var oidc = await OidcClaimAsync("tramites");

        oidc.Should().BeEquivalentTo(legacy);
        oidc.Should().BeEquivalentTo("it.plat.manage", "it.plat.extra", "it.reportes.read");
    }

    [PostgresFact]
    public async Task AC4_ParidadSinOverrides()
    {
        await SeedAsync(AdminCompanyId);

        (await OidcClaimAsync("tramites")).Should().BeEquivalentTo(await LegacyClaimAsync());
    }
}
