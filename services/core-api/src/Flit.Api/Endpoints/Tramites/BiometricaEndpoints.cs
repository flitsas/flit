using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

internal static class BiometricaEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesBiometricaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        // POST iniciar biométrica de una parte -> 201 { validation, token, magicLinkPath }
        group.MapPost("/instances/{id:guid}/biometric", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            [FromBody] IniciarBiometriaInput? body,
            IniciarBiometriaHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");
            if (body is null)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el cuerpo de la solicitud.");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, body, ct);
            return error switch
            {
                "datos_incompletos" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Completa nombre, tipo de documento, documento y email."),
                "parte_invalida" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "parte inválida (use comprador|vendedor o vacío)."),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se puede iniciar biométrica en estado draft."),
                "biometria_activa" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Ya existe una biométrica activa o aprobada para esta parte."),
                _ => Results.Created($"/api/v1/tramites/instances/{id}/biometric/{result!.Validation.Id}", result),
            };
        }).WithName("IniciarProcedureInstanceBiometric");

        // GET lista/estado de biométricas -> { validations: [...] }
        group.MapGet("/instances/{id:guid}/biometric", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            ListBiometriaHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found.")
                : Results.Ok(result);
        }).WithName("ListProcedureInstanceBiometric");

        return app;
    }
}
