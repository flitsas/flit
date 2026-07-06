using Flit.Api.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.SuperAdmin;

internal static class SuperAdminEndpointExtensions
{
    internal static IEndpointRouteBuilder MapSuperAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // HU #10508 AC3: mecanismo de autorización SuperAdmin unificado — un único policy real
        // basado en el rol "SuperAdmin" del JWT (AddApiSecurity). El stub por header
        // X-Flit-SuperAdmin (policy "SuperAdminOnly") se eliminó.
        var group = app.MapGroup("/api/v1/superadmin")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy);

        ProcedureTypeEndpoints.Map(group);
        CatalogEndpoints.Map(group);
        SecurityModulesEndpoints.Map(group);
        SecurityPermissionsEndpoints.Map(group);
        SecurityRolesEndpoints.Map(group);

        return app;
    }
}
