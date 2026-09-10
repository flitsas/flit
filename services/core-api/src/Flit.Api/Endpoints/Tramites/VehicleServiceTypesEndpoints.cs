using Flit.Tramites.Domain.Tramites.Catalog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Catálogo persistido de tipos de servicio del vehículo (<c>catalogs.vehicle_service_types</c>) —
/// sección 18 del FUR. Catálogo cerrado (6 valores): sin búsqueda, se devuelven todos los activos.
/// </summary>
internal static class VehicleServiceTypesEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesVehicleServiceTypesEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites").RequireAuthorization();

        group.MapGet("/vehicle-service-types", ListAsync)
            .WithName("ListVehicleServiceTypes")
            .WithSummary("Lista los tipos de servicio del vehículo (sección 18 del FUR)")
            .WithDescription(
                "GET /api/v1/tramites/vehicle-service-types — catálogo catalogs.vehicle_service_types. "
                + "Devuelve los activos ordenados por sort_order (orden normativo del FUR).")
            .Produces(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] IVehicleServiceTypeCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        var items = await catalog
            .ListActiveAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new
        {
            items = items.Select(t => new { id = t.Id, code = t.Code, name = t.Name, sortOrder = t.SortOrder }),
        });
    }
}
