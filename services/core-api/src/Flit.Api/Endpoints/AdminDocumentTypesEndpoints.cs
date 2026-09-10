using System.Security.Claims;
using Flit.Admin.Application.DocumentTypes;
using Flit.Admin.Application.DocumentTypes.CreateDocumentType;
using Flit.Admin.Application.DocumentTypes.DeleteDocumentType;
using Flit.Admin.Application.DocumentTypes.ListDocumentTypes;
using Flit.Admin.Application.DocumentTypes.PurgeDocumentType;
using Flit.Admin.Application.DocumentTypes.ReactivateDocumentType;
using Flit.Admin.Application.DocumentTypes.UpdateDocumentType;
using Flit.Admin.Domain.DocumentTypes;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints del catálogo maestro de tipos de documento (HU #10193).
/// Todo el grupo exige rol SuperAdmin (RF17 / AC5).
/// </summary>
public static class AdminDocumentTypesEndpoints
{
    public static IEndpointRouteBuilder MapAdminDocumentTypesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/document-types")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Documentos");

        // POST /api/v1/admin/document-types — alta (AC1 → 201 / 422).
        group.MapPost("/", CreateAsync)
            .WithName("AdminDocumentTypeCreate")
            .WithSummary("Crea un tipo de documento")
            .WithDescription("Da de alta un tipo de documento en el catálogo maestro. "
                + "Valida el nombre y genera el código. 422 si el payload es inválido. Requiere SuperAdmin.")
            .Produces<DocumentTypeResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // GET /api/v1/admin/document-types — listado paginado por nombre asc (AC2 → 200).
        group.MapGet("/", ListAsync)
            .WithName("AdminDocumentTypeList")
            .WithSummary("Lista tipos de documento")
            .WithDescription("Listado paginado del catálogo, ordenado por nombre ascendente. "
                + "Por defecto solo activos; usa includeInactive=true para incluir los desactivados. Requiere SuperAdmin.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // PUT /api/v1/admin/document-types/{id} — actualización (AC3 → 200 / 422 / 404).
        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("AdminDocumentTypeUpdate")
            .WithSummary("Actualiza un tipo de documento")
            .WithDescription("Modifica nombre y metadatos de un tipo de documento existente. "
                + "El código de sistema no cambia. 404 si no existe, 422 si el payload es inválido. Requiere SuperAdmin.")
            .Produces<DocumentTypeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // DELETE /api/v1/admin/document-types/{id} — soft-delete (AC4/AC6 → 204 / 409 / 404).
        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("AdminDocumentTypeDelete")
            .WithSummary("Desactiva un tipo de documento (soft-delete)")
            .WithDescription("Marca el documento como inactivo. 409 si está asociado a algún trámite "
                + "(no se puede desactivar en uso), 404 si no existe. Requiere SuperAdmin.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        // POST /api/v1/admin/document-types/{id}/reactivate — reactivación (→ 204 / 404).
        group.MapPost("/{id:guid}/reactivate", ReactivateAsync)
            .WithName("AdminDocumentTypeReactivate")
            .WithSummary("Reactiva un tipo de documento desactivado")
            .WithDescription("Contraparte del soft-delete: vuelve a marcar el documento como activo. "
                + "Idempotente (reactivar uno ya activo retorna 204), 404 si no existe. Requiere SuperAdmin.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}/permanent", PurgeAsync)
            .WithName("AdminDocumentTypePurge")
            .WithSummary("Elimina permanentemente un tipo de documento")
            .WithDescription("Borra el tipo y lo quita de requisitos, overrides y precedencia OT. "
                + "204 si se eliminó, 404 si no existe. Requiere SuperAdmin.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateDocumentTypeRequest request,
        HttpContext httpContext,
        [FromServices] CreateDocumentTypeHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateDocumentTypeCommand
        {
            Request = request,
            CreatedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsValid
            ? Results.Created($"/api/v1/admin/document-types/{result.Document!.Id}", result.Document)
            : Results.Json(new ErrorResponse(result.Error!), statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> ListAsync(
        [FromServices] ListDocumentTypesHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] bool? includeInactive = null,
        [FromQuery] string? q = null,
        [FromQuery] string? origen = null,
        [FromQuery] string? estado = null)
    {
        var query = new ListDocumentTypesQuery
        {
            Page = page,
            PageSize = pageSize,
            IncludeInactive = includeInactive,
            Search = q,
            Origen = origen,
            Estado = estado,
        };

        var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateDocumentTypeRequest request,
        HttpContext httpContext,
        [FromServices] UpdateDocumentTypeHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdateDocumentTypeCommand
        {
            Id = id,
            Request = request,
            UpdatedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            UpdateDocumentTypeOutcome.Updated => Results.Ok(result.Document),
            UpdateDocumentTypeOutcome.ValidationFailed => Results.Json(
                new ErrorResponse(result.Error!), statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.NotFound(new ErrorResponse($"No existe el tipo de documento {id}.")),
        };
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext httpContext,
        [FromServices] DeleteDocumentTypeHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new DeleteDocumentTypeCommand
        {
            Id = id,
            DeletedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            DeleteDocumentTypeOutcome.Deleted => Results.NoContent(),
            DeleteDocumentTypeOutcome.HasAssociations => Results.Json(
                new AssociationsErrorResponse(
                    DeleteDocumentTypeResult.HasAssociationsMessage, result.Associations),
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.NotFound(new ErrorResponse($"No existe el tipo de documento {id}.")),
        };
    }

    private static async Task<IResult> PurgeAsync(
        Guid id,
        [FromServices] PurgeDocumentTypeHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new PurgeDocumentTypeCommand { Id = id },
            cancellationToken).ConfigureAwait(false);

        return result.Outcome == PurgeDocumentTypeOutcome.Purged
            ? Results.NoContent()
            : Results.NotFound(new ErrorResponse($"No existe el tipo de documento {id}."));
    }

    private static async Task<IResult> ReactivateAsync(
        Guid id,
        HttpContext httpContext,
        [FromServices] ReactivateDocumentTypeHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new ReactivateDocumentTypeCommand
        {
            Id = id,
            UpdatedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            ReactivateDocumentTypeOutcome.Reactivated => Results.NoContent(),
            _ => Results.NotFound(new ErrorResponse($"No existe el tipo de documento {id}.")),
        };
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Cuerpo de error simple: <c>{ error: "mensaje" }</c> (422 / 404).</summary>
    private sealed record ErrorResponse(string Error);

    /// <summary>
    /// Cuerpo 409 del soft-delete bloqueado: el mensaje canónico (AC6) + la lista de trámites
    /// que usan el documento, para que la consola muestre dónde está en uso (mensaje accionable).
    /// </summary>
    private sealed record AssociationsErrorResponse(
        string Error,
        IReadOnlyList<DocumentTypeAssociationRef> ProcedureTypes);
}
