using Flit.Admin.Application.Improntas.ListImprontas;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints del historial de improntas (HU #10466/#10468 / ADR-0022, Feature #10462).
/// Únicamente el listado paginado/filtrable (HU #10468) — el endpoint de generación
/// (<c>POST /api/v1/admin/improntas/generate</c>, HU #10467) se agrega en otra rama; si se
/// mergea antes que ésta, el reviewer resuelve el conflicto trivial de este archivo (mismo
/// grupo <c>/api/v1/admin/improntas</c>).
/// </summary>
public static class AdminImprontasEndpoints
{
    public static IEndpointRouteBuilder MapAdminImprontasEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/improntas")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Improntas");

        // GET /api/v1/admin/improntas — listado paginado del historial (HU #10468).
        group.MapGet("", ListImprontasAsync)
            .WithName("AdminImprontasIndex")
            .WithSummary("Lista el historial de improntas generadas (paginado)")
            .WithDescription("Listado paginado y filtrable (placa, radicado, rango de fecha de creación) "
                + "del historial de improntas generadas (admin.impronta_generations), ordenado por fecha "
                + "de creación descendente. Vista global cross-tenant (sin RLS, ADR-0022): no re-expone el "
                + "PDF, solo metadata. Requiere SuperAdmin.")
            .Produces<ListImprontasResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> ListImprontasAsync(
        [FromServices] ListImprontasHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] string? placa = null,
        [FromQuery] string? radicado = null,
        [FromQuery] DateTimeOffset? createdFrom = null,
        [FromQuery] DateTimeOffset? createdTo = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var query = new ListImprontasQuery
        {
            Placa = placa,
            Radicado = radicado,
            CreatedFrom = createdFrom,
            CreatedTo = createdTo,
            Page = page,
            PageSize = pageSize,
        };

        var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }
}
