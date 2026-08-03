using Flit.Admin.Application.DocumentRequirements.PreviewInformativos;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

internal static class WizardEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesWizardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        group.MapGet("/instances/{id:guid}/wizard", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            GetWizardStateHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found.")
                : Results.Ok(result);
        }).WithName("GetProcedureInstanceWizardState");

        // CF-02 (HU #10883, AC3) — esqueleto de pasos para el PASO 1 cuando el trámite todavía no
        // existe. Mismos pasos/keys/etiquetas que el wizard real, con el paso 1 abierto y el resto
        // bloqueado: el trámite se crea al avanzar al paso 2. Sin tenant: no lee datos de negocio.
        group.MapGet("/wizard-preview", (string? modalidad) =>
        {
            var preview = GetWizardStateHandler.BuildPreview(modalidad);
            return preview is null
                ? Results.Problem(statusCode: 400, title: "Bad Request", detail: "Modalidad no válida.")
                : Results.Ok(preview);
        }).WithName("GetProcedureWizardPreview");

        // Guía informativa de documentos del trámite (paso 1, sin instancia).
        // modalidad: matricula_inicial | traspaso; transitOfficeId opcional (overrides OT).
        group.MapGet("/document-requirements/preview", async (
            string? modalidad,
            Guid? transitOfficeId,
            PreviewDocumentosInformativosHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new PreviewDocumentosInformativosQuery
                {
                    Modalidad = modalidad ?? string.Empty,
                    TransitOfficeId = transitOfficeId is null || transitOfficeId == Guid.Empty
                        ? null
                        : transitOfficeId,
                },
                ct);

            return result.Outcome switch
            {
                PreviewDocumentosInformativosOutcome.ModalidadInvalida =>
                    Results.Problem(statusCode: 400, title: "Bad Request", detail: "Modalidad no válida."),
                PreviewDocumentosInformativosOutcome.ProcedureTypeNotFound =>
                    Results.Problem(statusCode: 404, title: "Not Found", detail: "Tipo de trámite no encontrado."),
                _ => Results.Ok(new
                {
                    tipologiaCodigo = result.TipologiaCodigo,
                    procedureTypeId = result.ProcedureTypeId,
                    items = result.Items.Select(i => new
                    {
                        documentTypeId = i.DocumentTypeId,
                        codigo = i.Codigo,
                        nombre = i.Nombre,
                        obligatorio = i.Obligatorio,
                        orden = i.Orden,
                        descripcion = i.Descripcion,
                    }),
                }),
            };
        }).WithName("GetDocumentRequirementsPreview");

        return app;
    }
}
