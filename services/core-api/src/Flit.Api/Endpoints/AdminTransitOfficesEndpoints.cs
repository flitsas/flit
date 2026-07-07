using Flit.Admin.Application.Companies.TransitOffices.ListTransitOfficesOperationalStatus;
using Flit.Admin.Application.Companies.TransitOffices.SearchTransitOffices;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints del catálogo de organismos de tránsito (HU #10192, RF13, AC1). La búsqueda
/// exige rol SuperAdmin u ot_admin (módulo OT — HU #10218 / #10236); el estado operativo
/// (RF01) es exclusivo de SuperAdmin. El catálogo se lee desde
/// <c>catalogs.transit_offices</c> (BD).
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
            .WithDescription("Catálogo de Organismos de Tránsito (catalogs.transit_offices) con búsqueda "
                + "opcional por nombre/código vía el parámetro search. Requiere SuperAdmin u ot_admin.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // GET /api/v1/admin/transit-offices/operational-status — estado operativo por OT (RF01).
        // Exclusivo de SuperAdmin: añade la policy SuperAdmin sobre la del grupo (OtModule), de
        // modo que ot_admin —que sí ve el catálogo— queda fuera de la gestión del ciclo de vida.
        group.MapGet("/operational-status", ListOperationalStatusAsync)
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithName("AdminTransitOfficesOperationalStatus")
            .WithSummary("Estado operativo de los Organismos de Tránsito")
            .WithDescription("Por cada oficina del catálogo devuelve si tiene tenant OT dado de alta y, "
                + "si lo tiene, su estado activo/inactivo y modo de operación (join catalogs.transit_offices "
                + "+ admin.transit_office_profiles + identity.tenants). Requiere SuperAdmin.")
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

    private static async Task<IResult> ListOperationalStatusAsync(
        [FromServices] ListTransitOfficesOperationalStatusHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new ListTransitOfficesOperationalStatusQuery(), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }
}
