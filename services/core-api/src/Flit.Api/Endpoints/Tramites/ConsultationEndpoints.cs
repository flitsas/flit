using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

internal static class ConsultationEndpoints
{
    internal static IEndpointRouteBuilder MapConsultationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        group.MapPost("/instances/{id:guid}/consultations/{templateCode}", async (
            Guid id,
            string templateCode,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            RunConsultationHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, templateCode, ct);
            return error switch
            {
                "instance_not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "template_not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Consultation template not found."),
                "provider_not_resolved" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El template de consulta no declara un proveedor."),
                "provider_not_found" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El proveedor declarado no está registrado."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se pueden hidratar field_values en estado draft."),
                _ => Results.Ok(result)
            };
        }).WithName("RunConsultation");

        return app;
    }
}
