using System.Security.Claims;
using Flit.Admin.Domain.Companies;
using Microsoft.AspNetCore.Http;

namespace Flit.Api.Authorization;

/// <summary>
/// Lectura de bloqueos de OT (HU #12407 AC6): SuperAdmin, cabeza o hijo de la red en solo lectura.
/// Cliente ajeno → 403 sin revelar datos.
/// </summary>
public static class TransitBlocksReadAccess
{
    public static IResult Forbidden() =>
        Results.Json(
            new
            {
                error = "FORBIDDEN_TENANT",
                message = "No tienes permiso para consultar los bloqueos de esta compañía.",
            },
            statusCode: StatusCodes.Status403Forbidden);

    public static async Task<IResult?> EnsureReadAccessAsync(
        ClaimsPrincipal user,
        Guid headTenantId,
        ICompanyHierarchyRepository hierarchy,
        CancellationToken cancellationToken)
    {
        if (CompanyTenantAccess.IsSuperAdmin(user))
        {
            return null;
        }

        if (!RequestTenantResolver.TryResolveTenantId(user, out var callerTenantId))
        {
            return Results.Unauthorized();
        }

        if (callerTenantId == headTenantId)
        {
            var head = await hierarchy
                .GetHierarchyInfoAsync(headTenantId, cancellationToken)
                .ConfigureAwait(false);

            return head is { IsGroupParent: true, ParentTenantId: null } ? null : Forbidden();
        }

        var child = await hierarchy
            .GetHierarchyInfoAsync(callerTenantId, cancellationToken)
            .ConfigureAwait(false);

        return child?.ParentTenantId == headTenantId ? null : Forbidden();
    }
}
