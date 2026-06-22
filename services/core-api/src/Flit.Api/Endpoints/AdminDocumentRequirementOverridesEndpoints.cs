using System.Security.Claims;
using Flit.Admin.Application.DocumentRequirementOverrides.ListDocumentRequirementOverrides;
using Flit.Admin.Application.DocumentRequirementOverrides.SetDocumentRequirementOverride;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de la obligatoriedad documental por Organismo de Tránsito (HU #10198). Granular
/// solo para OT: cada documento asociado a un trámite puede marcarse, por OT, como obligatorio
/// (REQUIRED), opcional (OPTIONAL) o no aplica (NOT_APPLICABLE → se oculta de la matriz de ese
/// OT). El upsert por tupla natural usa estado=DEFAULT para limpiar el override. Todo el grupo
/// exige rol SuperAdmin.
/// </summary>
public static class AdminDocumentRequirementOverridesEndpoints
{
    public static IEndpointRouteBuilder MapAdminDocumentRequirementOverridesEndpoints(
        this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/document-requirement-overrides")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy);

        // GET ?procedureTypeId&transitOfficeId — overrides de obligatoriedad del trámite/OT.
        group.MapGet("/", ListAsync).WithName("AdminDocumentRequirementOverrideList");

        // PUT — upsert por tupla natural (estado=DEFAULT limpia) → 200 / 204 / 422 / 404.
        group.MapPut("/", SetAsync).WithName("AdminDocumentRequirementOverrideSet");

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] ListDocumentRequirementOverridesHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] Guid? procedureTypeId = null,
        [FromQuery] Guid? transitOfficeId = null)
    {
        if (procedureTypeId is null || procedureTypeId == Guid.Empty)
        {
            return BadRequest("El parámetro procedureTypeId es obligatorio.");
        }

        if (transitOfficeId is null || transitOfficeId == Guid.Empty)
        {
            return BadRequest("El parámetro transitOfficeId es obligatorio.");
        }

        var result = await handler
            .HandleAsync(
                new ListDocumentRequirementOverridesQuery
                {
                    ProcedureTypeId = procedureTypeId.Value,
                    TransitOfficeId = transitOfficeId.Value,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> SetAsync(
        SetDocumentRequirementOverrideRequest request,
        HttpContext httpContext,
        [FromServices] SetDocumentRequirementOverrideHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new SetDocumentRequirementOverrideCommand
        {
            Request = request,
            Actor = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            SetDocumentRequirementOverrideOutcome.Set => Results.Ok(result.Response),
            SetDocumentRequirementOverrideOutcome.Cleared => Results.NoContent(),
            SetDocumentRequirementOverrideOutcome.ValidationFailed => Results.Json(
                new ErrorResponse(result.Error!), statusCode: StatusCodes.Status422UnprocessableEntity),
            SetDocumentRequirementOverrideOutcome.ProcedureTypeNotFound => Results.NotFound(
                new ErrorResponse($"No existe el tipo de trámite {request.ProcedureTypeId}.")),
            SetDocumentRequirementOverrideOutcome.DocumentTypeNotFound => Results.NotFound(
                new ErrorResponse($"No existe el tipo de documento {request.DocumentTypeId}.")),
            _ => Results.NotFound(
                new ErrorResponse($"No existe el organismo de tránsito {request.TransitOfficeId}.")),
        };
    }

    private static IResult BadRequest(string message) =>
        Results.Json(new ErrorResponse(message), statusCode: StatusCodes.Status400BadRequest);

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Cuerpo de error simple: <c>{ error: "mensaje" }</c> (400 / 422 / 404).</summary>
    private sealed record ErrorResponse(string Error);
}
