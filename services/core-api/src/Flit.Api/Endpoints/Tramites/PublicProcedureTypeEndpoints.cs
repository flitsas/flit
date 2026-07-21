using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

        // FEATURE-08 / HU-BE-08 (CFD-12) — selector de operador (botón único). Requiere JWT
        // (RequireAuthorization → 401 sin token) y X-Tenant-Id (400 si falta). Retorna los tipos
        // publicados con su version. Catálogo global: no se filtra por tenant (AC-04).
        app.MapGet("/api/v1/procedure-types", async (
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            GetPublishedProcedureTypesHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var items = await handler.HandleAsync(ct);
            return Results.Ok(items);
        })
        .RequireAuthorization()
        .WithName("ListPublishedProcedureTypesForOperator");

        return app;
    }
}
