using System.Security.Claims;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// Tests unitarios del <see cref="PermissionAuthorizationHandler"/> (HU #10165).
///
/// No requieren WebApplicationFactory ni base de datos: se construye directamente
/// el <see cref="AuthorizationHandlerContext"/> con el <see cref="ClaimsPrincipal"/>
/// apropiado y se invoca el handler.
/// </summary>
public sealed class PermissionAuthorizationHandlerTests
{
    private readonly PermissionAuthorizationHandler _handler = new();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static AuthorizationHandlerContext BuildContext(
        ClaimsPrincipal user,
        PermissionRequirement requirement)
    {
        return new AuthorizationHandlerContext(
            [requirement],
            user,
            resource: null);
    }

    // ── AC4: SuperAdmin bypass ────────────────────────────────────────────────

    [Fact]
    public async Task AC4_SuperAdmin_SucceedsWithoutPermissionSlug()
    {
        // SuperAdmin no tiene el slug "tramites.read" en sus claims de permisos
        var user = BuildPrincipal(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role_code", "SuperAdmin"));

        var requirement = new PermissionRequirement("tramites.read");
        var context = BuildContext(user, requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue("SuperAdmin tiene bypass total (AC4)");
    }

    [Fact]
    public async Task AC4_SuperAdmin_CaseInsensitive_Succeeds()
    {
        var user = BuildPrincipal(
            new Claim("role_code", "superadmin"));

        var requirement = new PermissionRequirement("tramites.create");
        var context = BuildContext(user, requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue("el bypass SuperAdmin es case-insensitive");
    }

    // ── AC2: permiso presente en JWT ─────────────────────────────────────────

    [Fact]
    public async Task AC2_UserWithMatchingPermission_Succeeds()
    {
        var user = BuildPrincipal(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role_code", "Admin"),
            new Claim("permissions", "tramites.read"),
            new Claim("permissions", "tramites.create"));

        var requirement = new PermissionRequirement("tramites.read");
        var context = BuildContext(user, requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue("el JWT contiene el slug requerido");
    }

    [Fact]
    public async Task AC2_PermissionSlug_CaseInsensitive_Succeeds()
    {
        var user = BuildPrincipal(
            new Claim("role_code", "Operator"),
            new Claim("permissions", "TRAMITES.READ"));

        var requirement = new PermissionRequirement("tramites.read");
        var context = BuildContext(user, requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue("la comparación de slugs es case-insensitive");
    }

    // ── AC3: permiso ausente en JWT → no Succeed (403) ───────────────────────

    [Fact]
    public async Task AC3_UserWithoutRequiredPermission_DoesNotSucceed()
    {
        var user = BuildPrincipal(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role_code", "Admin"),
            new Claim("permissions", "tramites.create"));

        var requirement = new PermissionRequirement("tramites.read");
        var context = BuildContext(user, requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse("el slug 'tramites.read' no está en el JWT");
    }

    [Fact]
    public async Task AC3_UserWithDifferentPermission_DoesNotSucceed()
    {
        var user = BuildPrincipal(
            new Claim("role_code", "Operator"),
            new Claim("permissions", "users.read"),
            new Claim("permissions", "users.write"));

        var requirement = new PermissionRequirement("tramites.read");
        var context = BuildContext(user, requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse("el JWT no contiene el permiso requerido");
    }

    [Fact]
    public async Task AC3_UserWithNoPermissionClaims_DoesNotSucceed()
    {
        var user = BuildPrincipal(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role_code", "Admin"));
        // Sin claims "permissions"

        var requirement = new PermissionRequirement("tramites.read");
        var context = BuildContext(user, requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse("el JWT no tiene ningún claim 'permissions'");
    }

    [Fact]
    public async Task AC3_AnonymousUser_DoesNotSucceed()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity()); // sin autenticar, sin claims

        var requirement = new PermissionRequirement("tramites.read");
        var context = BuildContext(user, requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse("usuario sin claims no puede satisfacer el requirement");
    }

    // ── AC7: permiso desactivado → slug no viene en JWT ──────────────────────

    [Fact]
    public async Task AC7_DeactivatedPermission_NotInJwt_DoesNotSucceed()
    {
        // El slug no está en el JWT porque el login ya filtró el permiso desactivado
        var user = BuildPrincipal(
            new Claim("role_code", "Admin"),
            new Claim("permissions", "tramites.create")); // "tramites.read" filtrado en login

        var requirement = new PermissionRequirement("tramites.read");
        var context = BuildContext(user, requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse(
            "el JWT no lleva el slug de permiso desactivado; el JWT es la fuente de verdad en runtime (AC7)");
    }
}
