using Flit.Queries.Domain.Tenancy;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #12358 (Feature #12257) — policy «cabeza de grupo» de las rutas consolidadas
/// (<c>/api/v1/tramites/network/**</c>): el <c>TenantScope</c> que dejó el
/// <c>TenantEnforcementMiddleware</c> debe ser un grupo. <c>null</c> (ruta no scopeada), <c>All</c>
/// (SuperAdmin: ya tiene el global por sus rutas) o <c>Single</c> (cliente sin red) ⇒ 403
/// <c>{ error: "network_scope_required" }</c> sin ejecutar ninguna consulta. Se aplica al grupo de
/// rutas completo para que una ruta de red nueva no pueda olvidarse de la policy.
/// <para>
/// HU #12652 — segunda puerta, DESPUÉS del alcance: la red es exclusiva del rol
/// <see cref="NetworkScopePolicy.HeadAdminRole"/> (AdminCompany) de la cabeza. Un Radicador, Operador
/// u otro rol de la cabeza recibe 403 <c>{ error: "network_role_required" }</c> sin ejecutar ninguna
/// consulta; el rol se lee de los claims <c>role</c>/<c>role_code</c> del JWT (multi-rol: basta uno)
/// sin ir a la base — el alcance ya garantizó que el actor es cabeza sin padre. Hija, cliente sin red
/// y SuperAdmin siguen recibiendo <c>network_scope_required</c> (la primera puerta no cambió).
/// </para>
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

        if (NetworkScopePolicy.ValidateRole(RequestTenantResolver.RoleValues(context.HttpContext.User)) is { } roleError)
            return Results.Json(new { error = roleError }, statusCode: StatusCodes.Status403Forbidden);

        return await next(context);
    }
}
