using Flit.Admin.Application.DocumentOrderOverrides.GetResolvedDocumentMatrix;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoint de la matriz documental resuelta (HU #10196, RF18). Aplica la precedencia
/// Cliente &gt; OT &gt; Default y devuelve el orden final por documento. Exige rol SuperAdmin.
/// </summary>
public static class AdminResolvedDocumentMatrixEndpoints
{
    public static IEndpointRouteBuilder MapAdminResolvedDocumentMatrixEndpoints(
        this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/resolved-document-matrix")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy);

        // GET ?procedureTypeId&transitOfficeId?&clienteId? — matriz resuelta (AC3/AC4 → 200 / 400 / 404).
        group.MapGet("/", GetAsync).WithName("AdminResolvedDocumentMatrixGet");

        return app;
    }

    private static async Task<IResult> GetAsync(
        [FromServices] GetResolvedDocumentMatrixHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] Guid? procedureTypeId = null,
        [FromQuery] Guid? transitOfficeId = null,
        [FromQuery] Guid? clienteId = null)
    {
        if (procedureTypeId is null || procedureTypeId == Guid.Empty)
        {
            return Results.Json(
                new ErrorResponse("El parámetro procedureTypeId es obligatorio."),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await handler
            .HandleAsync(
                new GetResolvedDocumentMatrixQuery
                {
                    ProcedureTypeId = procedureTypeId.Value,
                    TransitOfficeId = transitOfficeId,
                    ClienteId = clienteId,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            GetResolvedDocumentMatrixOutcome.Resolved => Results.Ok(new { data = result.Data }),
            _ => Results.NotFound(new ErrorResponse($"No existe el tipo de trámite {procedureTypeId}.")),
        };
    }

    /// <summary>Cuerpo de error simple: <c>{ error: "mensaje" }</c> (400 / 404).</summary>
    private sealed record ErrorResponse(string Error);
}
