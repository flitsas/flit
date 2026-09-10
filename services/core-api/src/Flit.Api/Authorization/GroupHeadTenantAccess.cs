using System.Security.Claims;
using Flit.Admin.Application.Companies.Hierarchy;
using Flit.Admin.Domain.Companies;
using Microsoft.AspNetCore.Http;

namespace Flit.Api.Authorization;

/// <summary>
/// Acceso a rutas de cabeza de grupo (HU #12345). Complementa la policy
/// <see cref="AdminAuthorization.GroupHeadCompanyPolicy"/> con comprobaciones en handler.
/// </summary>
public static class GroupHeadTenantAccess
{
    public const string ForbiddenCode = "FORBIDDEN_GROUP_HEAD_SCOPE";

    public const string ForbiddenMessage =
        "No tienes permiso para operar sobre este cliente en el alcance de la red.";

    public static IResult Forbidden() =>
        Results.Json(
            new { error = ForbiddenCode, message = ForbiddenMessage },
            statusCode: StatusCodes.Status403Forbidden);

    public static IResult? UnauthorizedIfNoTenant(ClaimsPrincipal user, out Guid callerTenantId)
    {
        if (!RequestTenantResolver.TryResolveTenantId(user, out callerTenantId))
        {
            callerTenantId = Guid.Empty;
            return Results.Unauthorized();
        }

        return null;
    }

    public static async Task<IResult?> EnsureHeadCallerAsync(
        ClaimsPrincipal user,
        Guid headTenantId,
        ICompanyHierarchyRepository hierarchy,
        CancellationToken cancellationToken)
    {
        var unauthorized = UnauthorizedIfNoTenant(user, out var callerTenantId);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var guard = await GroupHeadParenthoodGuard
            .VerifyHeadCallerAsync(callerTenantId, headTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);

        return guard.IsAllowed ? null : Forbidden();
    }

    public static async Task<IResult?> EnsureChildOfHeadAsync(
        Guid headTenantId,
        Guid childTenantId,
        ICompanyHierarchyRepository hierarchy,
        CancellationToken cancellationToken)
    {
        var guard = await GroupHeadParenthoodGuard
            .VerifyChildOfHeadAsync(headTenantId, childTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);

        return guard.IsAllowed ? null : Forbidden();
    }

    public static async Task<IResult?> EnsureHeadAndChildAsync(
        ClaimsPrincipal user,
        Guid headTenantId,
        Guid childTenantId,
        ICompanyHierarchyRepository hierarchy,
        CancellationToken cancellationToken)
    {
        var headForbid = await EnsureHeadCallerAsync(user, headTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);
        if (headForbid is not null)
        {
            return headForbid;
        }

        return await EnsureChildOfHeadAsync(headTenantId, childTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);
    }
}
