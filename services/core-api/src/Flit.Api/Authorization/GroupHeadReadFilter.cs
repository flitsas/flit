using Flit.Queries.Domain.Tenancy;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #12358 (Feature #12257) — policy «cabeza de grupo» de las rutas consolidadas
/// (<c>/api/v1/tramites/network/**</c>): el <c>TenantScope</c> que dejó el
/// <c>TenantEnforcementMiddleware</c> debe ser un grupo. <c>null</c> (ruta no scopeada), <c>All</c>
/// (SuperAdmin: ya tiene el global por sus rutas) o <c>Single</c> (cliente sin red) ⇒ 403
/// <c>{ error: "network_scope_required" }</c> sin ejecutar ninguna consulta. Se aplica al grupo de
/// rutas completo para que una ruta de red nueva no pueda olvidarse de la policy.
/// </summary>
public sealed class GroupHeadReadFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var scope = RequestTenantResolver.ScopeFromItems(context.HttpContext);
        if (NetworkScopePolicy.Validate(scope) is { } error)
            return Results.Json(new { error }, statusCode: StatusCodes.Status403Forbidden);

        return await next(context);
    }
}
