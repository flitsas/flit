using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.SuperAdmin;

internal static class SuperAdminEndpointExtensions
{
    internal static IEndpointRouteBuilder MapSuperAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/superadmin")
            .RequireAuthorization("SuperAdminOnly");

        ProcedureTypeEndpoints.Map(group);
        CatalogEndpoints.Map(group);

        return app;
    }
}
