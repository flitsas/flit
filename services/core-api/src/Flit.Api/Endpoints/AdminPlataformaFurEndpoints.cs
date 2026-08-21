using Flit.Api.Authorization;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// SuperAdmin — Plataforma → FUR: preview sintético con el generador productivo (HU #11701).
/// </summary>
public static class AdminPlataformaFurEndpoints
{
    public static IEndpointRouteBuilder MapAdminPlataformaFurEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/plataforma/fur")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Plataforma · FUR");

        group.MapPost("/preview", PreviewAsync)
            .WithName("AdminPlataformaFurPreview")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> PreviewAsync(
        [FromBody] PreviewFurRequest? request,
        [FromServices] PreviewFurHandler handler,
        CancellationToken ct)
    {
        var result = await handler
            .HandleAsync(request ?? new PreviewFurRequest(null, null, null, null), ct)
            .ConfigureAwait(false);

        return result.Status switch
        {
            PreviewFurStatus.BadRequest => Results.Json(
                new { error = result.Error, allowed = result.Allowed },
                statusCode: StatusCodes.Status400BadRequest),
            PreviewFurStatus.NotFound => Results.NotFound(new { error = result.Error }),
            _ => Results.File(
                result.Document!.Content,
                contentType: "application/pdf",
                fileDownloadName: result.Document.Filename),
        };
    }
}
