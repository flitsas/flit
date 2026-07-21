using System.Security.Claims;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// LOG QX — control de acceso (HU #10794). El endpoint <c>GET /api/v1/admin/log-qx</c> se protege con
/// <c>.RequirePermission("logqx.read")</c>, que agrega un <see cref="PermissionRequirement"/> evaluado
/// por el <see cref="PermissionAuthorizationHandler"/>. Estos tests ejercen ese handler con el slug
/// <c>logqx.read</c> y cubren los AC de la HU: acceso permitido con el permiso (AC1), denegado sin él
/// (AC2, → 403) y el bypass de SuperAdmin. Mismo patrón que
/// <see cref="PermissionAuthorizationHandlerTests"/>: sin WebApplicationFactory ni base de datos.
/// </summary>
public sealed class LogQxAuthorizationTests
{
    private const string LogQxReadSlug = "logqx.read";

    private readonly PermissionAuthorizationHandler _handler = new();

    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "TestAuth"));

    private static AuthorizationHandlerContext BuildContext(
        ClaimsPrincipal user,
        PermissionRequirement requirement) =>
        new([requirement], user, resource: null);

    // ── AC1 — usuario con logqx.read accede (200) ────────────────────────────

    [Fact]
    public async Task AC1_UserWithLogQxRead_Succeeds()
    {
        var user = BuildPrincipal(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role_code", "Soporte"),
            new Claim("permissions", LogQxReadSlug));

        var requirement = new PermissionRequirement(LogQxReadSlug);
        var context = BuildContext(user, requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue("el JWT contiene el permiso logqx.read (AC1 → 200)");
    }

    // ── AC2 — autenticado sin logqx.read → no Succeed (el pipeline responde 403) ──

    [Fact]
    public async Task AC2_AuthenticatedUserWithoutLogQxRead_DoesNotSucceed()
    {
        // Usuario con otros permisos pero NO logqx.read (y sin rol SuperAdmin).
        var user = BuildPrincipal(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role_code", "AdminCompany"),
            new Claim("permissions", "tramites.read"),
            new Claim("permissions", "reportes.read"));

        var requirement = new PermissionRequirement(LogQxReadSlug);
        var context = BuildContext(user, requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse(
            "sin el permiso logqx.read el requirement no se satisface y el middleware emite 403 (AC2)");
    }

    // ── Bypass — SuperAdmin accede aunque no tenga el slug ───────────────────

    [Fact]
    public async Task SuperAdmin_WithoutSlug_Succeeds_ByBypass()
    {
        var user = BuildPrincipal(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role_code", "SuperAdmin"));

        var requirement = new PermissionRequirement(LogQxReadSlug);
        var context = BuildContext(user, requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue("SuperAdmin hace bypass total de permisos");
    }

    // ── Sin token — no Succeed (el 401 lo emite el pipeline de autenticación) ──

    [Fact]
    public async Task AnonymousUser_DoesNotSucceed()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        var requirement = new PermissionRequirement(LogQxReadSlug);
        var context = BuildContext(user, requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse(
            "un usuario sin autenticar no satisface el requirement (el pipeline responde 401)");
    }
}
