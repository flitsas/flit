using Flit.Admin.Application.Companies.TransitOffices.SearchTransitOffices;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoint del catálogo estático de organismos de tránsito (HU #10192, RF13, AC1).
/// Exige rol SuperAdmin u ot_admin (módulo OT — HU #10218 / #10236). El catálogo es
/// estático en memoria — no consulta BD.
/// </summary>
public static class AdminTransitOfficesEndpoints
{
    public static IEndpointRouteBuilder MapAdminTransitOfficesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/transit-offices")
            .RequireAuthorization(AdminAuthorization.OtModulePolicy)
            .WithTags("Admin · Compañías");

        // GET /api/v1/admin/transit-offices?search= — catálogo con búsqueda opcional (#10192 AC1).
        group.MapGet("", SearchTransitOfficesAsync)
            .WithName("AdminTransitOfficesSearch")
            .WithSummary("Busca en el catálogo de Organismos de Tránsito")
            .WithDescription("Catálogo estático (en memoria) de Organismos de Tránsito con búsqueda opcional "
                + "por nombre/código vía el parámetro search. Requiere SuperAdmin u ot_admin.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> SearchTransitOfficesAsync(
        [FromServices] SearchTransitOfficesHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] string? search = null)
    {
        var result = await handler
            .HandleAsync(new SearchTransitOfficesQuery { Search = search }, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }
}
