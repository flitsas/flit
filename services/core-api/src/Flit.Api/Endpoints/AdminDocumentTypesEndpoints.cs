using System.Security.Claims;
using Flit.Admin.Application.DocumentTypes.CreateDocumentType;
using Flit.Admin.Application.DocumentTypes.DeleteDocumentType;
using Flit.Admin.Application.DocumentTypes.ListDocumentTypes;
using Flit.Admin.Application.DocumentTypes.ReactivateDocumentType;
using Flit.Admin.Application.DocumentTypes.UpdateDocumentType;
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
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy);

        // POST /api/v1/admin/document-types — alta (AC1 → 201 / 422).
        group.MapPost("/", CreateAsync).WithName("AdminDocumentTypeCreate");

        // GET /api/v1/admin/document-types — listado paginado por nombre asc (AC2 → 200).
        group.MapGet("/", ListAsync).WithName("AdminDocumentTypeList");

        // PUT /api/v1/admin/document-types/{id} — actualización (AC3 → 200 / 422 / 404).
        group.MapPut("/{id:guid}", UpdateAsync).WithName("AdminDocumentTypeUpdate");

        // DELETE /api/v1/admin/document-types/{id} — soft-delete (AC4/AC6 → 204 / 409 / 404).
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("AdminDocumentTypeDelete");

        // POST /api/v1/admin/document-types/{id}/reactivate — reactivación (→ 204 / 404).
        group.MapPost("/{id:guid}/reactivate", ReactivateAsync).WithName("AdminDocumentTypeReactivate");

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
        [FromQuery] bool? includeInactive = null)
    {
        var query = new ListDocumentTypesQuery
        {
            Page = page,
            PageSize = pageSize,
            IncludeInactive = includeInactive,
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
                new ErrorResponse(DeleteDocumentTypeResult.HasAssociationsMessage),
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.NotFound(new ErrorResponse($"No existe el tipo de documento {id}.")),
        };
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

    /// <summary>Cuerpo de error simple: <c>{ error: "mensaje" }</c> (422 / 404 / 409).</summary>
    private sealed record ErrorResponse(string Error);
}
