using System.Security.Claims;
using Flit.Admin.Application.DocumentOrderOverrides;
using Flit.Admin.Application.DocumentOrderOverrides.CreateDocumentOrderOverride;
using Flit.Admin.Application.DocumentOrderOverrides.DeleteDocumentOrderOverride;
using Flit.Admin.Application.DocumentOrderOverrides.ListDocumentOrderOverrides;
using Flit.Admin.Application.DocumentOrderOverrides.UpdateDocumentOrderOverride;
using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de los overrides de orden documental por OT (HU #10196; RF22 — el OT es el
/// único ámbito). Todo el grupo exige rol SuperAdmin. El ámbito (<c>scope=OT</c>) viaja como
/// query; la referencia se toma de <c>transitOfficeId</c>.
/// </summary>
public static class AdminDocumentOrderOverridesEndpoints
{
    public static IEndpointRouteBuilder MapAdminDocumentOrderOverridesEndpoints(
        this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/document-order-overrides")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Órdenes documentales");

        // POST ?scope=OT|CLIENTE — crea el override (AC1/AC2 → 201 / 422 / 404 / 400).
        group.MapPost("/", CreateAsync)
            .WithName("AdminDocumentOrderOverrideCreate")
            .WithSummary("Crea un override de orden por OT")
            .WithDescription("Define un orden personalizado para un documento en un organismo de tránsito. "
                + "El query scope=OT es el único ámbito (RF22); la referencia viaja en transitOfficeId. "
                + "400 si falta scope; 404 si trámite/documento/OT no existen; 422 si el payload es inválido. "
                + "Requiere SuperAdmin.")
            .Produces<DocumentOrderOverrideResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // GET ?procedureTypeId&scope&transitOfficeId — lista por OT (AC5 → 200 / 400).
        group.MapGet("/", ListAsync)
            .WithName("AdminDocumentOrderOverrideList")
            .WithSummary("Lista overrides de orden por OT")
            .WithDescription("Retorna los overrides de orden de un trámite para un organismo de tránsito. "
                + "Requiere procedureTypeId, scope=OT y transitOfficeId; 400 si falta alguno. Requiere SuperAdmin.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // PUT /{id} — reordenamiento por arrastre: persiste el nuevo orden (→ 200 / 422 / 404).
        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("AdminDocumentOrderOverrideUpdate")
            .WithSummary("Actualiza el orden de un override")
            .WithDescription("Persiste el nuevo valor de orden de un override (reordenamiento por arrastre). "
                + "404 si no existe, 422 si el orden es inválido. Requiere SuperAdmin.")
            .Produces<DocumentOrderOverrideResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // DELETE /{id} — borrado físico (AC6 → 204 / 404).
        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("AdminDocumentOrderOverrideDelete")
            .WithSummary("Elimina un override de orden")
            .WithDescription("Borra el override; el documento vuelve a heredar el orden por defecto del trámite. "
                + "404 si no existe. Requiere SuperAdmin.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateDocumentOrderOverrideRequest request,
        HttpContext httpContext,
        [FromServices] CreateDocumentOrderOverrideHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] string? scope = null)
    {
        var normalizedScope = DocumentOrderScope.Normalize(scope);
        if (normalizedScope is null)
        {
            return Results.Json(
                new ErrorResponse("El parámetro scope es obligatorio y debe ser OT (RF22)."),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var command = new CreateDocumentOrderOverrideCommand
        {
            Scope = normalizedScope,
            Request = request,
            CreatedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            CreateDocumentOrderOverrideOutcome.Created => Results.Created(
                $"/api/v1/admin/document-order-overrides/{result.Response!.Id}", result.Response),
            CreateDocumentOrderOverrideOutcome.ValidationFailed => Results.Json(
                new ErrorResponse(result.Error!), statusCode: StatusCodes.Status422UnprocessableEntity),
            CreateDocumentOrderOverrideOutcome.ProcedureTypeNotFound => Results.NotFound(
                new ErrorResponse($"No existe el tipo de trámite {request.ProcedureTypeId}.")),
            CreateDocumentOrderOverrideOutcome.DocumentTypeNotFound => Results.NotFound(
                new ErrorResponse($"No existe el tipo de documento {request.DocumentTypeId}.")),
            _ => Results.NotFound(new ErrorResponse(
                $"No existe el organismo de tránsito {request.TransitOfficeId}.")),
        };
    }

    private static async Task<IResult> ListAsync(
        [FromServices] ListDocumentOrderOverridesHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] Guid? procedureTypeId = null,
        [FromQuery] string? scope = null,
        [FromQuery] Guid? transitOfficeId = null)
    {
        if (procedureTypeId is null || procedureTypeId == Guid.Empty)
        {
            return BadRequest("El parámetro procedureTypeId es obligatorio.");
        }

        var normalizedScope = DocumentOrderScope.Normalize(scope);
        if (normalizedScope is null)
        {
            return BadRequest("El parámetro scope es obligatorio y debe ser OT (RF22).");
        }

        // RF22 — el único ámbito es OT.
        if (transitOfficeId is null || transitOfficeId == Guid.Empty)
        {
            return BadRequest("El parámetro transitOfficeId es obligatorio para scope OT.");
        }

        var scopeRefId = transitOfficeId.Value;

        var result = await handler
            .HandleAsync(
                new ListDocumentOrderOverridesQuery
                {
                    ProcedureTypeId = procedureTypeId.Value,
                    Scope = normalizedScope,
                    ScopeRefId = scopeRefId,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateDocumentOrderOverrideRequest request,
        [FromServices] UpdateDocumentOrderOverrideHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(
                new UpdateDocumentOrderOverrideCommand { Id = id, Orden = request.Orden },
                cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            UpdateDocumentOrderOverrideOutcome.Updated => Results.Ok(result.Response),
            UpdateDocumentOrderOverrideOutcome.ValidationFailed => Results.Json(
                new ErrorResponse(result.Error!), statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.NotFound(new ErrorResponse($"No existe el override {id}.")),
        };
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        [FromServices] DeleteDocumentOrderOverrideHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new DeleteDocumentOrderOverrideCommand { Id = id }, cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            DeleteDocumentOrderOverrideOutcome.Deleted => Results.NoContent(),
            _ => Results.NotFound(new ErrorResponse($"No existe el override {id}.")),
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
