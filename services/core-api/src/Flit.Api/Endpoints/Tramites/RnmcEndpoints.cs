using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// FEATURE 05 — consulta RNMC (medidas correctivas) DESACOPLADA del pre-vuelo. Se dispara en el paso
/// final del wizard (cuando la fecha de expedición de cada actor ya está capturada), no en el pre-vuelo.
/// </summary>
internal static class RnmcEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesRnmcEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        // Corre la consulta RNMC por cada actor natural (con su fecha de expedición) y persiste el
        // resultado. Idempotente: re-consultar refresca el resultado.
        group.MapPost("/instances/{id:guid}/rnmc", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            RunRnmcConsultHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "migrado_solo_lectura" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Trámite migrado (solo lectura): no se corren consultas RNMC."),
                _ => Results.Ok(result),
            };
        }).WithName("RunProcedureInstanceRnmc");

        // Devuelve el último resultado RNMC persistido (lista de checks por actor), o vacío.
        group.MapGet("/instances/{id:guid}/rnmc", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            GetRnmcHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found.")
                : Results.Ok(result);
        }).WithName("GetProcedureInstanceRnmc");

        return app;
    }
}
