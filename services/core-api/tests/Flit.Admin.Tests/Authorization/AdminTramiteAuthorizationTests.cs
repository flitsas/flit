using System.Security.Claims;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// Catálogo de permisos para acciones de gestión avanzada del administrador sobre trámites
/// (Feature #12155, HU #12157). Los 6 slugs (<see cref="AdminTramiteAuthorization.AllSlugs"/>)
/// se enforcean con el mismo mecanismo genérico ya existente (HU #10165):
/// <see cref="PermissionRequirement"/> evaluado por <see cref="PermissionAuthorizationHandler"/>.
///
/// Esta HU no implementa los endpoints de negocio (los agregan las HUs dependientes
/// #12158-#12162); estos tests ejercen el handler directamente con cada slug nuevo, igual que
/// <see cref="LogQxAuthorizationTests"/>, para dejar probado el punto de enforcement que esas
/// HUs van a consumir.
/// </summary>
public sealed class AdminTramiteAuthorizationTests
{
    private readonly PermissionAuthorizationHandler _handler = new();

    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "TestAuth"));

    private static AuthorizationHandlerContext BuildContext(
        ClaimsPrincipal user,
        PermissionRequirement requirement) =>
        new([requirement], user, resource: null);

    public static TheoryData<string> AllSlugs()
    {
        var data = new TheoryData<string>();
        foreach (var slug in AdminTramiteAuthorization.AllSlugs)
        {
            data.Add(slug);
        }

        return data;
    }

    // ── AC1 — rol con el slug requerido → la autorización pasa ───────────────

    [Theory]
    [MemberData(nameof(AllSlugs))]
    public async Task AC1_RoleWithSlug_Succeeds(string slug)
    {
        var user = BuildPrincipal(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role_code", "AdminTramitesOperador"),
            new Claim("permissions", slug));

        var requirement = new PermissionRequirement(slug);
        var context = BuildContext(user, requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue(
            $"el JWT contiene el slug {slug} requerido por el endpoint (AC1)");
    }

    // ── AC2 — rol sin el slug requerido → no Succeed (el pipeline responde 403) ──

    [Theory]
    [MemberData(nameof(AllSlugs))]
    public async Task AC2_RoleWithoutSlug_DoesNotSucceed(string slug)
    {
        var user = BuildPrincipal(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role_code", "AdminCompany"),
            new Claim("permissions", "tramites.read"));

        var requirement = new PermissionRequirement(slug);
        var context = BuildContext(user, requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse(
            $"el JWT no contiene {slug}; el middleware debe responder 403 (AC2)");
    }

    // ── AC3 — slugs independientes: tener uno no habilita otro ───────────────

    [Fact]
    public async Task AC3_RoleWithAnular_DoesNotSucceedForCambiarEstado()
    {
        var user = BuildPrincipal(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role_code", "AdminTramitesOperador"),
            new Claim("permissions", AdminTramiteAuthorization.AnularSlug));

        var anularRequirement = new PermissionRequirement(AdminTramiteAuthorization.AnularSlug);
        var anularContext = BuildContext(user, anularRequirement);
        await _handler.HandleAsync(anularContext);
        anularContext.HasSucceeded.Should().BeTrue(
            "el rol tiene AdminTramiteAnular y debe pasar al invocar el endpoint de anular (AC3)");

        var cambiarEstadoRequirement = new PermissionRequirement(AdminTramiteAuthorization.CambiarEstadoSlug);
        var cambiarEstadoContext = BuildContext(user, cambiarEstadoRequirement);
        await _handler.HandleAsync(cambiarEstadoContext);
        cambiarEstadoContext.HasSucceeded.Should().BeFalse(
            "tener AdminTramiteAnular NO debe habilitar AdminTramiteCambiarEstado (AC3)");
    }

    [Theory]
    [MemberData(nameof(AllSlugs))]
    public async Task AC3_RoleWithOneSlug_DoesNotSucceedForAnyOtherSlug(string grantedSlug)
    {
        var user = BuildPrincipal(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role_code", "AdminTramitesOperador"),
            new Claim("permissions", grantedSlug));

        foreach (var otherSlug in AdminTramiteAuthorization.AllSlugs)
        {
            if (otherSlug == grantedSlug)
            {
                continue;
            }

            var requirement = new PermissionRequirement(otherSlug);
            var context = BuildContext(user, requirement);

            await _handler.HandleAsync(context);

            context.HasSucceeded.Should().BeFalse(
                $"tener {grantedSlug} no debe habilitar {otherSlug} (AC3 — slugs independientes)");
        }
    }

    // ── Bypass — SuperAdmin accede aunque no tenga el slug ───────────────────

    [Theory]
    [MemberData(nameof(AllSlugs))]
    public async Task SuperAdmin_WithoutSlug_Succeeds_ByBypass(string slug)
    {
        var user = BuildPrincipal(
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("role_code", "SuperAdmin"));

        var requirement = new PermissionRequirement(slug);
        var context = BuildContext(user, requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue("SuperAdmin hace bypass total de permisos");
    }

    // ── Sin token — no Succeed ────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllSlugs))]
    public async Task AnonymousUser_DoesNotSucceed(string slug)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        var requirement = new PermissionRequirement(slug);
        var context = BuildContext(user, requirement);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse(
            "un usuario sin autenticar no satisface el requirement (el pipeline responde 401)");
    }

    private static readonly string[] ExpectedSlugs =
    [
        "AdminTramiteCambiarEstado",
        "AdminTramiteAnular",
        "AdminTramiteLimpiarConsolidado",
        "AdminTramiteCargarConsolidado",
        "AdminTramiteReenviarValidacion",
        "AdminTramiteReasignarGestor",
    ];

    // ── Catálogo — exactamente los 6 slugs esperados por la HU, sin duplicados ──

    [Fact]
    public void Catalog_ContainsExactlySixExpectedSlugs()
    {
        AdminTramiteAuthorization.AllSlugs.Should().BeEquivalentTo(ExpectedSlugs);

        AdminTramiteAuthorization.AllSlugs.Should().OnlyHaveUniqueItems();
    }
}
