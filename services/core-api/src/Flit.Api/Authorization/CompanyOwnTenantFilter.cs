using Microsoft.AspNetCore.Http;

namespace Flit.Api.Authorization;

/// <summary>
/// Filtro de endpoint: AdminCompany solo opera su <c>tenant_id</c>; SuperAdmin libre.
/// Espera un argumento de ruta/handler <c>Guid tenantId</c> (HU #11228).
/// </summary>
public sealed class CompanyOwnTenantFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        Guid? tenantId = null;
        foreach (var arg in context.Arguments)
        {
            if (arg is Guid g)
            {
                tenantId = g;
                break;
            }
        }

        if (tenantId is { } tid)
        {
            var forbid = CompanyTenantAccess.ForbidIfForeignTenant(context.HttpContext.User, tid);
            if (forbid is not null)
            {
                return forbid;
            }
        }

        return await next(context).ConfigureAwait(false);
    }
}
