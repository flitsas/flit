using Flit.Tramites.Domain.Tramites.Catalog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Catálogo persistido de colores de vehículo (<c>catalogs.vehicle_colors</c>) para el
/// selector de transformaciones del wizard. Búsqueda server-side (no se descarga el catálogo completo).
/// </summary>
internal static class VehicleColorsEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesVehicleColorsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites").RequireAuthorization();

        group.MapGet("/vehicle-colors", SearchAsync)
            .WithName("SearchVehicleColors")
            .WithSummary("Busca colores de vehículo en el catálogo RUNT")
            .WithDescription(
                "GET /api/v1/tramites/vehicle-colors?search=&limit=50 — catálogo catalogs.vehicle_colors. "
                + "Sin search devuelve los primeros N por nombre. Máximo limit=100.")
            .Produces(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<IResult> SearchAsync(
        [FromServices] IVehicleColorCatalog catalog,
        [FromQuery] string? search = null,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var items = await catalog
            .SearchAsync(search, limit, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new
        {
            items = items.Select(c => new { id = c.Id, code = c.Code, name = c.Name }),
        });
    }
}
