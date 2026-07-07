using System.Security.Claims;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// Tests unitarios del <see cref="AdminCompanyAuthorizationHandler"/>.
///
/// No requieren WebApplicationFactory ni base de datos: se construye directamente
/// el <see cref="AuthorizationHandlerContext"/> con el <see cref="ClaimsPrincipal"/>
/// apropiado y se invoca el handler.
/// </summary>
public sealed class AdminCompanyAuthorizationHandlerTests
{
    private readonly AdminCompanyAuthorizationHandler _handler = new();

    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static AuthorizationHandlerContext BuildContext(ClaimsPrincipal user) =>
        new([new AdminCompanyRequirement()], user, resource: null);

    [Fact]
    public async Task AdminCompanyRole_Succeeds()
    {
        var user = BuildPrincipal(new Claim(AdminAuthorization.RoleClaimType, AdminAuthorization.AdminCompanyRole));
        var context = BuildContext(user);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task SuperAdminRole_Succeeds()
    {
        var user = BuildPrincipal(new Claim(AdminAuthorization.RoleClaimType, AdminAuthorization.SuperAdminRole));
        var context = BuildContext(user);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task OtherRole_DoesNotSucceed()
    {
        var user = BuildPrincipal(new Claim(AdminAuthorization.RoleClaimType, "Radicador"));
        var context = BuildContext(user);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    // Fix post-review #10504 — multi-rol (HU #10506): el JWT emite un claim "role" POR CADA rol
    // activo, en orden no determinístico. FindFirst (usado antes del fix) solo evalúa el
    // PRIMERO, así que un usuario con AdminCompany/SuperAdmin en una posición != 0 perdía acceso.
    [Fact]
    public async Task Fix10504_MultiRole_AdminCompanyNotFirstClaim_StillSucceeds()
    {
        var user = BuildPrincipal(
            new Claim(AdminAuthorization.RoleClaimType, "Radicador"),
            new Claim(AdminAuthorization.RoleClaimType, AdminAuthorization.AdminCompanyRole));
        var context = BuildContext(user);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue(
            "deben evaluarse TODOS los claims 'role', no solo el primero");
    }

    [Fact]
    public async Task Fix10504_MultiRole_SuperAdminNotFirstClaim_StillSucceeds()
    {
        var user = BuildPrincipal(
            new Claim(AdminAuthorization.RoleClaimType, "Radicador"),
            new Claim(AdminAuthorization.RoleClaimType, AdminAuthorization.SuperAdminRole));
        var context = BuildContext(user);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue(
            "deben evaluarse TODOS los claims 'role', no solo el primero");
    }
}
