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

        // Lookup dedicado de persona en RUNT (CONDUCTOR) para autopoblar el comprador. NO persiste.
        // found:false también responde 200 (el frontend cae al ingreso manual).
        group.MapPost("/instances/{id:guid}/runt-person", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            RuntPersonLookupRequest request,
            RuntPersonLookupHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(
                id, tenantId.Value, request.DocumentType, request.DocumentNumber, ct);

            return error switch
            {
                "invalid_request" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Se requiere documentType y documentNumber."),
                "instance_not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "provider_not_found" => Results.Problem(statusCode: 503, title: "Service Unavailable", detail: "El proveedor RUNT conductor no está disponible."),
                "unsupported_document_type" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "Tipo de documento no soportado por RUNT conductor (NIT no aplica)."),
                _ => Results.Ok(result)
            };
        }).WithName("RuntPersonLookup");

        return app;
    }
}

/// <summary>Body de POST /instances/{id}/runt-person — documento a consultar en RUNT.</summary>
internal sealed record RuntPersonLookupRequest(string? DocumentType, string? DocumentNumber);
