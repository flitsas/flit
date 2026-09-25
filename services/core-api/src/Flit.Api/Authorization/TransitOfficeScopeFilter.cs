using Flit.Admin.Domain.OtProfile;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Api.Authorization;

/// <summary>
/// Bug #12912 (review PR #442, IDOR) — filtro de grupo para rutas con <c>{transitOfficeId}</c>: un
/// usuario que no es SuperAdmin solo opera el organismo de su propio perfil OT. Misma regla y mismo
/// cuerpo 403 que <c>EnforceTransitOfficeScopeAsync</c> de
/// <c>AdminOtPrendaDocumentPolicyEndpoints</c> / <c>ForbidIfOfficeOutOfScopeAsync</c> de
/// <c>AdminOtMandatosEndpoints</c>, pero aplicada una sola vez al grupo para que ninguna ruta nueva
/// quede sin guarda.
/// </summary>
public sealed class TransitOfficeScopeFilter : IEndpointFilter
{
    public const string RouteKey = "transitOfficeId";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        if (http.User.IsInRole(AdminAuthorization.SuperAdminRole))
        {
            return await next(context).ConfigureAwait(false);
        }

        if (http.Request.RouteValues.TryGetValue(RouteKey, out var raw)
            && Guid.TryParse(raw?.ToString(), out var transitOfficeId)
            && RequestTenantResolver.TryResolveTenantId(http.User, out var tenantId))
        {
            var profiles = http.RequestServices.GetRequiredService<IOtProfileRepository>();
            var profile = await profiles
                .GetByTenantAsync(tenantId, http.RequestAborted)
                .ConfigureAwait(false);
            if (profile is not null && profile.TransitOfficeId == transitOfficeId)
            {
                return await next(context).ConfigureAwait(false);
            }
        }

        return Results.Json(
            new { code = "TRANSIT_OFFICE_FORBIDDEN", message = AdminAuthorization.OtModuleForbiddenMessage },
            statusCode: StatusCodes.Status403Forbidden);
    }
}
