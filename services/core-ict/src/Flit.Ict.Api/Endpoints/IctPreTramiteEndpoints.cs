using Flit.Ict.Api.Authorization;
using Flit.Ict.Application.Edit;

namespace Flit.Ict.Api.Endpoints;

/// <summary>Endpoints de gestión del pre-trámite (edición) — <c>/api/v1/pretramites</c>.</summary>
public static class IctPreTramiteEndpoints
{
    /// <summary>Body de edición parcial (v2, camelCase). rowVersion es obligatorio (concurrencia).</summary>
    public sealed record EditPreTramiteRequest(
        long RowVersion,
        string? DeliveryAddress = null,
        string? ManagerMail = null,
        string? SellingDate = null,
        decimal? SellingPrice = null,
        string? TrafficSecretaryCode = null,
        bool? ProcessWithoutAttachedDocuments = null);

    public static IEndpointRouteBuilder MapIctPreTramiteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/pretramites").RequireAuthorization(IctSecurityExtensions.IctClientPolicy);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            EditPreTramiteRequest body,
            EditPreTramiteHandler handler,
            CancellationToken ct) =>
        {
            var command = new EditPreTramiteCommand(
                id,
                body.RowVersion,
                body.DeliveryAddress,
                body.ManagerMail,
                body.SellingDate,
                body.SellingPrice,
                body.TrafficSecretaryCode,
                body.ProcessWithoutAttachedDocuments);

            var (result, error) = await handler.HandleAsync(command, ct);
            if (error is not null)
            {
                return error switch
                {
                    "not_found" => Results.Json(new { error }, statusCode: StatusCodes.Status404NotFound),
                    "already_materialized" or "not_editable" or "stale" =>
                        Results.Json(new { error }, statusCode: StatusCodes.Status409Conflict),
                    "unauthenticated" => Results.Json(new { error }, statusCode: StatusCodes.Status401Unauthorized),
                    _ => Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest),
                };
            }

            return Results.Ok(result);
        });

        return app;
    }
}
