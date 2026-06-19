using System.Security.Claims;
using Flit.Admin.Application.ProcedureInstances.CreateProcedureInstance;
using Flit.Admin.Application.ProcedureSnapshots.GetProcedureDocumentRequirements;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de trámites con snapshot documental inmutable (HU #10197, RF19). Exigen
/// usuario autenticado (cualquier JWT válido), <b>no</b> SuperAdmin: el operador del tenant
/// crea y consulta los documentos de su trámite.
///
/// El POST captura la matriz documental resuelta vigente y la congela junto al trámite en
/// una sola transacción (AC1/AC3). El GET devuelve los documentos del snapshot, no de la
/// configuración live, garantizando la inmutabilidad ante cambios posteriores (AC2/AC4).
/// </summary>
public static class TramitesEndpoints
{
    public static IEndpointRouteBuilder MapTramitesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/tramites")
            .RequireAuthorization();

        // POST — crea el trámite y congela el snapshot (AC1/AC3 → 201 / 422 / 404 / 500).
        group.MapPost("/", CreateAsync).WithName("TramitesCreate");

        // GET /{tramiteId}/document-requirements — documentos del snapshot (AC2/AC4 → 200 / 404).
        group.MapGet("/{tramiteId:guid}/document-requirements", GetDocumentRequirementsAsync)
            .WithName("TramitesGetDocumentRequirements");

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateProcedureInstanceRequest request,
        HttpContext httpContext,
        [FromServices] CreateProcedureInstanceHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateProcedureInstanceCommand
        {
            Request = request,
            ActorUserId = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            CreateProcedureInstanceOutcome.Created => Results.Created(
                $"/api/v1/tramites/{result.Response!.Id}", result.Response),
            CreateProcedureInstanceOutcome.ValidationFailed => Results.Json(
                new ErrorResponse(result.Error!), statusCode: StatusCodes.Status422UnprocessableEntity),
            CreateProcedureInstanceOutcome.ProcedureTypeNotFound => Results.NotFound(
                new ErrorResponse($"No existe el tipo de trámite {request.ProcedureTypeId}.")),
            CreateProcedureInstanceOutcome.TenantNotFound => Results.NotFound(
                new ErrorResponse($"No existe el cliente {request.TenantId}.")),
            _ => Results.Json(
                new ErrorResponse(result.Error!), statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<IResult> GetDocumentRequirementsAsync(
        Guid tramiteId,
        [FromServices] GetProcedureDocumentRequirementsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new GetProcedureDocumentRequirementsQuery { TramiteId = tramiteId }, cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            GetProcedureDocumentRequirementsOutcome.Found => Results.Ok(new { data = result.Data }),
            _ => Results.NotFound(
                new ErrorResponse($"No existe el trámite {tramiteId} o no tiene snapshot documental.")),
        };
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Cuerpo de error simple: <c>{ error: "mensaje" }</c> (422 / 404 / 500).</summary>
    private sealed record ErrorResponse(string Error);
}
