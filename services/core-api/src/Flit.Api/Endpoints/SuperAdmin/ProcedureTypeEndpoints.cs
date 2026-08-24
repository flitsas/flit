using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Flit.Modules.Quipux.Application.UseCases.MapeoTipoTramite;

namespace Flit.Api.Endpoints.SuperAdmin;

internal static class ProcedureTypeEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/procedure-types", async (
            string? family,
            string? publicationStatus,
            ListProcedureTypesHandler handler,
            CancellationToken ct) =>
        {
            var items = await handler.HandleAsync(family, publicationStatus, ct);
            return Results.Ok(items);
        }).WithName("ListProcedureTypes");

        group.MapPost("/procedure-types", async (
            CreateProcedureTypeRequest request,
            CreateProcedureTypeHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(request, ct);
            return error switch
            {
                "invalid_code" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "El código debe ir en MAYÚSCULAS, con letras, dígitos o guion bajo, y entre 3 y 60 caracteres. Es la llave con la que el tipo viaja a las integraciones."),
                "invalid_name" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "El nombre del tipo es obligatorio: es el rótulo del trámite en el mandato y en la portada del expediente."),
                "invalid_family" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Familia inválida: use MATRICULAS, TRASPASO u OTROS."),
                "code_taken" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Ya existe un tipo de trámite con ese código."),
                _ => Results.Created($"/api/v1/superadmin/procedure-types/{result!.Id}", result),
            };
        }).WithName("CreateProcedureType");

        group.MapGet("/procedure-types/{id:guid}", async (
            Guid id,
            GetProcedureTypeHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, ct);
            return result is null
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found.")
                : Results.Ok(result);
        }).WithName("GetProcedureType");

        group.MapPut("/procedure-types/{id:guid}", async (
            Guid id,
            UpdateProcedureTypeRequest request,
            UpdateProcedureTypeHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(id, request, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                "invalid_family" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Familia inválida: use MATRICULAS, TRASPASO u OTROS."),
                "conflict" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Cannot update a published procedure type."),
                _ => Results.Ok(result)
            };
        }).WithName("UpdateProcedureType");

        group.MapDelete("/procedure-types/{id:guid}", async (
            Guid id,
            DeleteProcedureTypeHandler handler,
            CancellationToken ct) =>
        {
            var error = await handler.HandleAsync(id, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                "conflict" => Results.Problem(statusCode: 409, title: "Conflict", detail: "No se puede retirar un tipo que tiene trámites: quedarían apuntando a un tipo archivado."),
                _ => Results.NoContent()
            };
        }).WithName("DeleteProcedureType");

        group.MapPost("/procedure-types/{id:guid}/publish", async (
            Guid id,
            PublishProcedureTypeHandler handler,
            CancellationToken ct) =>
        {
            var (result, error, validationErrors) = await handler.HandleAsync(id, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                "already_published" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Procedure type is already published."),
                "validation_failed" => Results.UnprocessableEntity(validationErrors),
                _ => Results.Ok(result)
            };
        }).WithName("PublishProcedureType");

        group.MapPost("/procedure-types/{id:guid}/archive", async (
            Guid id,
            ArchiveProcedureTypeHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(id, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                "already_archived" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Procedure type is already archived."),
                _ => Results.Ok(result)
            };
        }).WithName("ArchiveProcedureType");

        // ADR-0050 — barrera de operación: si el gestor puede elegir este tipo al crear un trámite.
        // Va aparte del PUT del tipo a propósito: aquel congela la definición al publicar, y los tipos
        // del catálogo están publicados, así que por ahí la barrera nunca se habría podido mover.
        group.MapPut("/procedure-types/{id:guid}/wizard-enabled", async (
            Guid id,
            SetWizardEnabledBody body,
            SetWizardEnabledHandler handler,
            CancellationToken ct) =>
        {
            var (result, error, detail) = await handler.HandleAsync(id, body.Enabled, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                // 422 y no 409: no es un conflicto de estado sino una precondición de negocio, y el
                // cuerpo trae la lista de lo que falta para poder habilitarlo.
                SetWizardEnabledHandler.NotReady => Results.UnprocessableEntity(detail),
                _ => Results.Ok(result)
            };
        }).WithName("SetProcedureTypeWizardEnabled");

        // ── Equivalencias con sistemas externos (ADR-0050) ────────────────────
        // Viven en `procedure_types.external_refs`, con el resto de la parametrización del tipo: un
        // solo punto de configuración en vez de un catálogo por integración.

        group.MapGet("/procedure-types/{id:guid}/quipux-mapping", async (
            Guid id,
            ObtenerMapeoQuipuxHandler handler,
            CancellationToken ct) =>
        {
            var (mapeo, error) = await handler.HandleAsync(id, ct);
            if (error == "not_found")
                return Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found.");

            // Sin bloque, el tipo no se radica en la secretaría. Es un estado legítimo, no un vacío
            // que haya que reportar como error.
            return mapeo is null ? Results.NoContent() : Results.Ok(mapeo);
        }).WithName("GetProcedureTypeQuipuxMapping");

        group.MapPut("/procedure-types/{id:guid}/quipux-mapping", async (
            Guid id,
            MapeoQuipuxDto? body,
            GuardarMapeoQuipuxHandler handler,
            CancellationToken ct) =>
        {
            var (mapeo, error) = await handler.HandleAsync(id, body, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                GuardarMapeoQuipuxHandler.NoUtilizable => Results.Problem(
                    statusCode: 422,
                    title: "Unprocessable Entity",
                    detail: "El mapeo está incompleto: revise familia, código de trámite, código de requisito, prefijo y tope de empresa. Un bloque a medias deja el trámite sin radicar."),
                _ => mapeo is null ? Results.NoContent() : Results.Ok(mapeo),
            };
        }).WithName("SetProcedureTypeQuipuxMapping");

        group.MapGet("/procedure-types/{id:guid}/conformation-rules", async (
            Guid id,
            GetConformationRulesHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(id, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found.")
                : Results.Ok(result);
        }).WithName("GetConformationRules");

        group.MapPut("/procedure-types/{id:guid}/conformation-rules", async (
            Guid id,
            List<ConformationRuleInput> inputs,
            UpsertConformationRulesHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(id, inputs, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                string e when e.StartsWith("entity_not_found:") =>
                    Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: $"Procedure entity not found: {e[17..]}"),
                _ => Results.Ok(result)
            };
        }).WithName("UpsertConformationRules");

        // FEATURE-08 / HU-BE-01 (CFD-01) — perfil de conformación del tipo (gate_profile +
        // conformationRules + sources + documentRequirements). GET lo lee completo; PUT lo actualiza
        // solo en estado draft (published/archived → 422).
        group.MapGet("/procedure-types/{id:guid}/conformation-profile", async (
            Guid id,
            GetConformationProfileHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(id, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found.")
                : Results.Ok(result);
        }).WithName("GetConformationProfile");

        group.MapPut("/procedure-types/{id:guid}/conformation-profile", async (
            Guid id,
            UpdateConformationProfileInput input,
            UpdateConformationProfileHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(id, input, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                "invalid_entry_mode" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "entryMode inválido: use PLATE, VIN o BOTH."),
                "not_editable" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "Tipo en estado published — no editable."),
                string e when e.StartsWith("entity_not_found:") =>
                    Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: $"Actor no encontrado en el catálogo: {e["entity_not_found:".Length..]}"),
                string e when e.StartsWith("source_not_found:") =>
                    Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: $"Fuente externa no encontrada en el catálogo: {e["source_not_found:".Length..]}"),
                string e when e.StartsWith("document_type_not_found:") =>
                    Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: $"Tipo de documento no encontrado en el catálogo: {e["document_type_not_found:".Length..]}"),
                _ => Results.Ok(result)
            };
        }).WithName("UpdateConformationProfile");

        group.MapGet("/procedure-types/{id:guid}/steps", async (
            Guid id,
            GetProcedureStepsHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(id, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found.")
                : Results.Ok(result);
        }).WithName("GetProcedureSteps");

        group.MapPut("/procedure-types/{id:guid}/steps", async (
            Guid id,
            List<ProcedureStepInput> inputs,
            UpsertProcedureStepsHandler handler,
            CancellationToken ct) =>
        {
            var (result, error, lockedViolated) = await handler.HandleAsync(id, inputs, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                "locked_field_violation" => Results.Problem(
                    statusCode: 409,
                    title: "Conflict",
                    detail: $"Cannot remove locked fields: {string.Join(", ", lockedViolated ?? [])}"),
                _ => Results.Ok(result)
            };
        }).WithName("UpsertProcedureSteps");

        group.MapPost("/procedure-types/{id:guid}/validate", async (
            Guid id,
            ValidateProcedureTypeHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(id, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found.")
                : Results.Ok(result);
        }).WithName("ValidateProcedureType");
    }
}

/// <summary>Cuerpo de PUT /procedure-types/{id}/wizard-enabled.</summary>
internal sealed record SetWizardEnabledBody(bool Enabled);
