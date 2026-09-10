using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #12320 (Feature #12254) — punto único de resolución del cliente de la petición.
/// Uso de ejemplo:
/// <code>
/// if (!RequestTenantResolver.TryResolveTenantId(http.User, out var tenantId)) return Results.Unauthorized();
/// var (tenant, isSuperAdmin) = RequestTenantResolver.FromItems(http);
/// </code>
/// AC1 (misma semántica que las copias locales sustituidas) y AC4 (contrato de Items del middleware).
/// </summary>
public sealed class RequestTenantResolverTests
{
    private const string CompanyTenant = "11111111-1111-1111-1111-111111111111";
    private const string OtherTenant = "99999999-9999-9999-9999-999999999999";

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));

    // ── TryResolveTenantId (AC1: mismo resultado que las copias locales) ─────────────

    [Fact]
    public void TryResolveTenantId_ClaimValido_ResuelveElMismoGuid()
    {
        var user = Principal(new Claim(AdminAuthorization.TenantIdClaimType, CompanyTenant));

        RequestTenantResolver.TryResolveTenantId(user, out var tenantId).Should().BeTrue();
        tenantId.Should().Be(Guid.Parse(CompanyTenant));
    }

    [Fact]
    public void TryResolveTenantId_SinClaim_FalsoYGuidEmpty()
    {
        var user = Principal(new Claim(AdminAuthorization.RoleClaimType, "Radicador"));

        RequestTenantResolver.TryResolveTenantId(user, out var tenantId).Should().BeFalse();
        tenantId.Should().Be(Guid.Empty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-un-guid")]
    [InlineData("1111")]
    public void TryResolveTenantId_ClaimMalFormado_Falso(string raw)
    {
        var user = Principal(new Claim(AdminAuthorization.TenantIdClaimType, raw));

        RequestTenantResolver.TryResolveTenantId(user, out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolveTenantId_GuidEmpty_CuentaComoResuelto_ComoLasCopiasLocales()
    {
        // Nota documentada en el componente: ninguna copia local validaba Guid.Empty; para que cada
        // sitio de llamada devuelva EXACTAMENTE el mismo resultado (AC1) se conserva ese comportamiento.
        var user = Principal(new Claim(AdminAuthorization.TenantIdClaimType, Guid.Empty.ToString()));

        RequestTenantResolver.TryResolveTenantId(user, out var tenantId).Should().BeTrue();
        tenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TryResolveNonEmptyTenantId_GuidEmpty_Falso()
    {
        // Regla del middleware y de la telemetría: vacío = "sin compañía asignada".
        var user = Principal(new Claim(AdminAuthorization.TenantIdClaimType, Guid.Empty.ToString()));

        RequestTenantResolver.TryResolveNonEmptyTenantId(user, out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolveTenantId_UsaElPrimerClaim_ComoFindFirstValue()
    {
        // Las copias usaban FindFirstValue: con dos claims tenant_id gana el primero. Mismo contrato.
        var user = Principal(
            new Claim(AdminAuthorization.TenantIdClaimType, CompanyTenant),
            new Claim(AdminAuthorization.TenantIdClaimType, OtherTenant));

        RequestTenantResolver.TryResolveTenantId(user, out var tenantId).Should().BeTrue();
        tenantId.Should().Be(Guid.Parse(CompanyTenant));
    }

    [Fact]
    public void ResolveTenantIdOrNull_ContratoNullable()
    {
        RequestTenantResolver.ResolveTenantIdOrNull(
            Principal(new Claim(AdminAuthorization.TenantIdClaimType, CompanyTenant)))
            .Should().Be(Guid.Parse(CompanyTenant));
        RequestTenantResolver.ResolveTenantIdOrNull(Principal()).Should().BeNull();
    }

    [Fact]
    public void TryResolveTenantId_UserNull_LanzaArgumentNull()
    {
        var act = () => RequestTenantResolver.TryResolveTenantId(null!, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── IsSuperAdmin (misma regla del middleware: TODOS los claims role / role_code) ──

    [Fact]
    public void IsSuperAdmin_PorClaimRole_Verdadero()
    {
        RequestTenantResolver.IsSuperAdmin(Principal(new Claim(AdminAuthorization.RoleClaimType, "SuperAdmin")))
            .Should().BeTrue();
    }

    [Fact]
    public void IsSuperAdmin_PorClaimRoleCode_Verdadero()
    {
        RequestTenantResolver.IsSuperAdmin(Principal(new Claim("role_code", "superadmin")))
            .Should().BeTrue("la comparación es OrdinalIgnoreCase, igual que en el middleware");
    }

    [Fact]
    public void IsSuperAdmin_MultiRolNoEsElPrimerClaim_SigueSiendoReconocido()
    {
        // HU #10506 / fix #10504: se evalúan TODOS los claims de rol, no solo el primero.
        var user = Principal(
            new Claim(AdminAuthorization.RoleClaimType, "Radicador"),
            new Claim(AdminAuthorization.RoleClaimType, "SuperAdmin"));

        RequestTenantResolver.IsSuperAdmin(user).Should().BeTrue();
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("Radicador")]
    [InlineData("ot_admin")]
    public void IsSuperAdmin_OtrosRoles_Falso(string role)
    {
        RequestTenantResolver.IsSuperAdmin(Principal(new Claim(AdminAuthorization.RoleClaimType, role)))
            .Should().BeFalse();
    }

    [Fact]
    public void IsSuperAdmin_ValorSuperAdminEnOtroTipoDeClaim_NoCuenta()
    {
        // Solo role / role_code cuentan; un claim arbitrario con el valor no concede el bypass.
        RequestTenantResolver.IsSuperAdmin(Principal(new Claim("permissions", "SuperAdmin")))
            .Should().BeFalse();
    }

    // ── FromItems (AC4: claves, tipos y semántica null = todos, sin cambios) ─────────

    [Fact]
    public void FromItems_SinItems_NullYNoSuperAdmin()
    {
        var http = new DefaultHttpContext();

        var (tenantId, isSuperAdmin) = RequestTenantResolver.FromItems(http);

        tenantId.Should().BeNull("fuera de las rutas scopeadas el middleware no deja Items");
        isSuperAdmin.Should().BeFalse();
    }

    [Fact]
    public void FromItems_CompanyUser_LeeLasMismasClavesQueDejaElMiddleware()
    {
        var http = new DefaultHttpContext();
        http.Items[TenantEnforcementMiddleware.TenantItemKey] = Guid.Parse(CompanyTenant);
        http.Items[TenantEnforcementMiddleware.SuperAdminItemKey] = false;

        var (tenantId, isSuperAdmin) = RequestTenantResolver.FromItems(http);

        tenantId.Should().Be(Guid.Parse(CompanyTenant));
        isSuperAdmin.Should().BeFalse();
    }

    [Fact]
    public void FromItems_SuperAdminSinAcotar_TenantNullEsTodos()
    {
        var http = new DefaultHttpContext();
        http.Items[TenantEnforcementMiddleware.TenantItemKey] = (Guid?)null;
        http.Items[TenantEnforcementMiddleware.SuperAdminItemKey] = true;

        var (tenantId, isSuperAdmin) = RequestTenantResolver.FromItems(http);

        tenantId.Should().BeNull();
        isSuperAdmin.Should().BeTrue();
    }

    [Fact]
    public void FromItems_ContratoDeClaves_NoCambia()
    {
        // AC4/AC5: las claves literales son parte del contrato entre middleware y endpoints.
        TenantEnforcementMiddleware.TenantItemKey.Should().Be("tramites.tenantId");
        TenantEnforcementMiddleware.SuperAdminItemKey.Should().Be("tramites.isSuperAdmin");
    }

    // ── Middleware → resolver (AC4: end-to-end con DefaultHttpContext) ───────────────

    private static async Task<(bool NextCalled, DefaultHttpContext Ctx)> RunMiddlewareAsync(
        ClaimsPrincipal? user, string? headerTenant = null, string path = "/api/v1/tramites/instances")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        if (user is not null)
            ctx.User = user;
        if (headerTenant is not null)
            ctx.Request.Headers["X-Tenant-Id"] = headerTenant;

        var nextCalled = false;
        var mw = new TenantEnforcementMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        await mw.InvokeAsync(ctx);
        return (nextCalled, ctx);
    }

    [Fact]
    public async Task Middleware_SuperAdminConHeader_ItemsConservanElTenantDelHeader()
    {
        var user = Principal(
            new Claim(AdminAuthorization.RoleClaimType, "SuperAdmin"),
            new Claim(AdminAuthorization.TenantIdClaimType, CompanyTenant));

        var (next, ctx) = await RunMiddlewareAsync(user, headerTenant: OtherTenant);

        next.Should().BeTrue();
        var (tenantId, isSuperAdmin) = RequestTenantResolver.FromItems(ctx);
        tenantId.Should().Be(Guid.Parse(OtherTenant));
        isSuperAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task Middleware_SuperAdminSinHeader_ItemsTenantNull()
    {
        var (next, ctx) = await RunMiddlewareAsync(Principal(new Claim("role_code", "SuperAdmin")));

        next.Should().BeTrue();
        var (tenantId, isSuperAdmin) = RequestTenantResolver.FromItems(ctx);
        tenantId.Should().BeNull();
        isSuperAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task Middleware_CompanyUserConClaim_SobreescribeHeaderYDejaItems()
    {
        var user = Principal(
            new Claim(AdminAuthorization.RoleClaimType, "AdminCompany"),
            new Claim(AdminAuthorization.TenantIdClaimType, CompanyTenant));

        var (next, ctx) = await RunMiddlewareAsync(user, headerTenant: OtherTenant);

        next.Should().BeTrue();
        ctx.Request.Headers["X-Tenant-Id"].ToString().Should().Be(CompanyTenant);
        var (tenantId, isSuperAdmin) = RequestTenantResolver.FromItems(ctx);
        tenantId.Should().Be(Guid.Parse(CompanyTenant));
        isSuperAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task Middleware_CompanyUserSinClaim_403()
    {
        var (next, ctx) = await RunMiddlewareAsync(Principal(new Claim(AdminAuthorization.RoleClaimType, "AdminCompany")));

        next.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Middleware_CompanyUserConClaimGuidEmpty_403()
    {
        // El middleware SIEMPRE rechazó Guid.Empty (a diferencia de las copias locales): se conserva.
        var user = Principal(
            new Claim(AdminAuthorization.RoleClaimType, "AdminCompany"),
            new Claim(AdminAuthorization.TenantIdClaimType, Guid.Empty.ToString()));

        var (next, ctx) = await RunMiddlewareAsync(user);

        next.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    // ── Lista declarativa (AC3: enumerable, con matching idéntico al histórico) ─────

    [Theory]
    [InlineData("/api/v1/tramites/instances", true)]
    [InlineData("/api/v1/tramites/instances/22222222-2222-2222-2222-222222222222/fur", true)]
    [InlineData("/api/v1/tramites/INSTANCES", true)]
    [InlineData("/api/v1/tramites/transit-offices", true)]
    [InlineData("/api/v1/tramites/transit-offices/algo", false)]
    [InlineData("/api/v1/tramites/preflight-preview", true)]
    [InlineData("/api/v1/tramites/preflight-preview/x", false)]
    [InlineData("/api/v1/tramites/rues-preview", true)]
    [InlineData("/api/v1/tramites/biometric-validations/22222222-2222-2222-2222-222222222222/resend", true)]
    [InlineData("/api/v1/tramites/plate-preassign/available", true)]
    [InlineData("/api/v1/tramites/identity-validation/stuck", true)]
    [InlineData("/api/v1/tramites/deeds", true)]
    [InlineData("/api/v1/tramites/legal-representatives/by-nit", true)]
    [InlineData("/api/v1/tramites/actors/contact-lookup", true)]
    [InlineData("/api/v1/tramites/procedure-types", false)]
    [InlineData("/api/v1/tramites/instancesx", false)]
    [InlineData("/api/v1/admin/companies", false)]
    [InlineData("/api/v1/public/tramites/instances", false)]
    public void IsRuntimeScoped_MatchingIdenticoAlHistorico(string path, bool expected)
    {
        TenantEnforcementMiddleware.IsRuntimeScoped(new PathString(path)).Should().Be(expected);
    }

    [Fact]
    public void RuntimeScopedRoutes_ConservaLos10PrefijosYSusComparaciones()
    {
        var routes = TenantEnforcementMiddleware.RuntimeScopedRoutes;

        routes.Select(r => (r.Path, r.Match)).Should().BeEquivalentTo(new[]
        {
            ("/api/v1/tramites/instances", TenantEnforcementMiddleware.RouteMatch.Prefix),
            ("/api/v1/tramites/transit-offices", TenantEnforcementMiddleware.RouteMatch.Exact),
            ("/api/v1/tramites/preflight-preview", TenantEnforcementMiddleware.RouteMatch.Exact),
            ("/api/v1/tramites/rues-preview", TenantEnforcementMiddleware.RouteMatch.Exact),
            ("/api/v1/tramites/biometric-validations", TenantEnforcementMiddleware.RouteMatch.Prefix),
            ("/api/v1/tramites/plate-preassign", TenantEnforcementMiddleware.RouteMatch.Prefix),
            ("/api/v1/tramites/identity-validation", TenantEnforcementMiddleware.RouteMatch.Prefix),
            ("/api/v1/tramites/deeds", TenantEnforcementMiddleware.RouteMatch.Prefix),
            ("/api/v1/tramites/legal-representatives", TenantEnforcementMiddleware.RouteMatch.Prefix),
            ("/api/v1/tramites/actors", TenantEnforcementMiddleware.RouteMatch.Prefix),
        });
        routes.Should().OnlyContain(r => r.Path.StartsWith(TenantEnforcementMiddleware.RuntimeRoutePrefix + "/", StringComparison.Ordinal));
    }
}
