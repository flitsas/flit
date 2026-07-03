using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

internal static class CommercialEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesCommercialEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        group.MapGet("/instances/{id:guid}/commercial", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            GetCommercialHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found.")
                : Results.Ok(result); // result puede ser null (sin comercial aún).
        }).WithName("GetProcedureInstanceCommercial");

        group.MapPut("/instances/{id:guid}/commercial", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            CommercialDto request,
            PutCommercialHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, request, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se pueden editar datos comerciales en estado borrador."),
                "invalid_valor_venta" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "valorVenta debe ser mayor a cero."),
                "invalid_causal" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "causal inválida (use COMPRAVENTA|DONACION|DACION_EN_PAGO|ADJUDICACION)."),
                "invalid_tasa" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "tasaImpuesto no puede ser negativa."),
                "invalid_derechos" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "derechos no puede ser negativo."),
                _ => Results.Ok(result),
            };
        }).WithName("PutProcedureInstanceCommercial");

        return app;
    }
}
