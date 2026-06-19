using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

internal static class PublicProcedureTypeEndpoints
{
    internal static IEndpointRouteBuilder MapPublicProcedureTypeEndpoints(this IEndpointRouteBuilder app)
    {
        // Listado público para Operación (HU #10200, AC1): solo tipos publicados.
        // El query param publicationStatus se ignora server-side y se fuerza a 'published'
        // para NO exponer draft/archived a usuarios normales.
        app.MapGet("/api/v1/tramites/procedure-types", async (
            string? family,
            ListProcedureTypesHandler handler,
            CancellationToken ct) =>
        {
            var items = await handler.HandleAsync(family, "published", ct);
            return Results.Ok(items);
        }).WithName("ListPublishedProcedureTypes");

        return app;
    }
}
