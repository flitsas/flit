using Flit.Tramites.Application.UseCases.Catalogs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.SuperAdmin;

internal static class CatalogEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/procedure-entities", async (
            ListProcedureEntitiesHandler handler,
            CancellationToken ct) =>
        {
            var items = await handler.HandleAsync(ct);
            return Results.Ok(items);
        }).WithName("ListProcedureEntities");

        group.MapGet("/external-data-sources", async (
            ListExternalDataSourcesHandler handler,
            CancellationToken ct) =>
        {
            var items = await handler.HandleAsync(ct);
            return Results.Ok(items);
        }).WithName("ListExternalDataSources");

        group.MapGet("/consultation-templates", async (
            ListConsultationTemplatesHandler handler,
            CancellationToken ct) =>
        {
            var items = await handler.HandleAsync(ct);
            return Results.Ok(items);
        }).WithName("ListConsultationTemplates");

        group.MapPost("/consultation-templates/{id:guid}/apply-fields", async (
            Guid id,
            ApplyTemplateFieldsRequest request,
            ApplyConsultationTemplateFieldsHandler handler,
            CancellationToken ct) =>
        {
            var error = await handler.HandleAsync(id, request, ct);
            return error switch
            {
                "template_not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Consultation template not found."),
                "section_not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Target section not found."),
                _ => Results.Ok()
            };
        }).WithName("ApplyConsultationTemplateFields");
    }
}
