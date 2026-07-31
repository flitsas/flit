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
            CancellationToken ct,
            // HU #10885 (Feature #10862, CF-04, botón "Actualizar"): query param opcional, default
            // false (cero regresión). En true, salta el reúso de caché y fuerza reconsulta + recacheo.
            [FromQuery] bool forceRefresh = false) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, templateCode, forceRefresh, ct);
            return error switch
            {
                "instance_not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "template_not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Consultation template not found."),
                "provider_not_resolved" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El template de consulta no declara un proveedor."),
                "provider_not_found" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El proveedor declarado no está registrado."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se pueden hidratar field_values en borrador o con subsanación activa."),
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

        // HU #10611 (Feature #10587) — la compañía valida el SOAT re-consultando el RUNT del vehículo
        // con el trámite en 'asignado'. Marca soat_estado (vigente/vencido/unknown) sin cambiar de estado.
        group.MapPost("/instances/{id:guid}/soat/validate-runt", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            ValidateSoatViaRuntHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error switch
            {
                "instance_not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "invalid_state" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El SOAT solo se valida con el trámite en estado 'asignado'."),
                "template_not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Consultation template RUNT_VEHICLE not found."),
                "provider_not_resolved" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El template RUNT_VEHICLE no declara un proveedor."),
                "provider_not_found" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El proveedor RUNT del vehículo no está registrado."),
                _ => Results.Ok(result)
            };
        }).WithName("ValidateSoatViaRunt");

        // Lookup JURÍDICO en RUES por NIT (bifurcación del "Consultar RUNT" para persona jurídica).
        // NO persiste. found:false también responde 200 (el frontend cae al ingreso manual).
        group.MapPost("/instances/{id:guid}/rues-lookup", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            RuesPersonLookupRequest request,
            RuesPersonLookupHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, request.DocumentNumber, ct);

            return error switch
            {
                "invalid_request" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Se requiere documentNumber (NIT)."),
                "instance_not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "provider_not_found" => Results.Problem(statusCode: 503, title: "Service Unavailable", detail: "El proveedor RUES no está disponible."),
                _ => Results.Ok(result)
            };
        }).WithName("RuesPersonLookup");

        return app;
    }
}

/// <summary>Body de POST /instances/{id}/runt-person — documento a consultar en RUNT.</summary>
internal sealed record RuntPersonLookupRequest(string? DocumentType, string? DocumentNumber);

/// <summary>Body de POST /instances/{id}/rues-lookup — NIT de la persona jurídica a consultar en RUES.</summary>
internal sealed record RuesPersonLookupRequest(string? DocumentNumber);
