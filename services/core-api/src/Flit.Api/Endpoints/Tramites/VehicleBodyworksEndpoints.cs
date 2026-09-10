using Flit.Tramites.Domain.Tramites.Catalog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Catálogo persistido de carrocerías (<c>catalogs.vehicle_bodyworks</c>) para el selector
/// de cambio de carrocería. Filtra por clase del vehículo consultado.
/// </summary>
internal static class VehicleBodyworksEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesVehicleBodyworksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites").RequireAuthorization();

        group.MapGet("/vehicle-bodyworks", SearchAsync)
            .WithName("SearchVehicleBodyworks")
            .WithSummary("Lista carrocerías del catálogo RUNT filtradas por clase de vehículo")
            .WithDescription(
                "GET /api/v1/tramites/vehicle-bodyworks?vehicleClass=&search=&limit=200 — "
                + "con vehicleClass solo las de esa clase; sin clase, el respaldo sin class_vehicle. "
                + "Máximo limit=300.")
            .Produces(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<IResult> SearchAsync(
        [FromServices] IVehicleBodyworkCatalog catalog,
        [FromQuery] string? vehicleClass = null,
        [FromQuery] string? search = null,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var items = await catalog
            .SearchAsync(vehicleClass, search, limit, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new
        {
            items = items.Select(c => new
            {
                id = c.Id,
                code = c.Code,
                name = c.Name,
                classVehicle = c.ClassVehicle,
            }),
        });
    }
}
