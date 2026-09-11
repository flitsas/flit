using Flit.Admin.Application.Ict;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Catálogo SuperAdmin de jobs ICT y últimos runs (HU #12513).
/// Prefijo <c>/api/v1/admin/ict</c> — no <c>/api/v1/ict</c> (YARP → core-ict).
/// </summary>
public static class AdminIctJobCatalogEndpoints
{
    public static IEndpointRouteBuilder MapAdminIctJobCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/ict")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · ICT · Jobs");

        group.MapGet("/jobs", GetCatalogAsync)
            .WithName("AdminIctGetJobCatalog")
            .WithSummary("Catálogo de jobs ICT (metadatos + último run, sin PII)")
            .Produces<IReadOnlyList<IctJobCatalogItemView>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/jobs/{jobKey}/runs", GetRunsAsync)
            .WithName("AdminIctGetJobRuns")
            .WithSummary("Últimas corridas de un job de pipeline (take acotado, sin PII)")
            .Produces<IReadOnlyList<IctJobRunListItemView>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetCatalogAsync(
        [FromServices] GetIctJobCatalogHandler handler,
        CancellationToken cancellationToken)
    {
        var items = await handler.HandleAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(items);
    }

    private static async Task<IResult> GetRunsAsync(
        string jobKey,
        [FromQuery] int? take,
        [FromServices] GetIctJobRunsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(jobKey, take, cancellationToken).ConfigureAwait(false);
        return result.Exists ? Results.Ok(result.Items) : Results.NotFound();
    }
}
