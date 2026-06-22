using System.Security.Claims;
using Flit.Admin.Application.DocumentRequirements;
using Flit.Admin.Application.DocumentRequirements.CreateProcedureDocumentRequirement;
using Flit.Admin.Application.DocumentRequirements.DeleteProcedureDocumentRequirement;
using Flit.Admin.Application.DocumentRequirements.ListProcedureDocumentRequirements;
using Flit.Admin.Application.DocumentRequirements.UpdateProcedureDocumentRequirement;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de la asociación de documentos a tipos de trámite (HU #10195).
/// Todo el grupo exige rol SuperAdmin (RF17 / AC5).
/// </summary>
public static class AdminProcedureDocumentRequirementsEndpoints
{
    public static IEndpointRouteBuilder MapAdminProcedureDocumentRequirementsEndpoints(
        this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/procedure-document-requirements")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Documentos por Trámite");

        // POST — asocia documento a trámite (AC1 → 201 / 422 / 404).
        group.MapPost("/", CreateAsync)
            .WithName("AdminProcedureDocumentRequirementCreate")
            .WithSummary("Asocia un documento a un tipo de trámite")
            .WithDescription("Crea la asociación (trámite, documento) con su orden default y obligatoriedad. "
                + "404 si el trámite o el documento no existen; 422 si el documento está inactivo o el par ya existe. "
                + "Requiere SuperAdmin.")
            .Produces<ProcedureDocumentRequirementResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // GET ?procedureTypeId={guid} — lista documentos del trámite (AC2 → 200 / 400).
        group.MapGet("/", ListAsync)
            .WithName("AdminProcedureDocumentRequirementList")
            .WithSummary("Lista los documentos de un tipo de trámite")
            .WithDescription("Retorna las asociaciones del trámite ordenadas por orden default ascendente, "
                + "enriquecidas con los datos del documento. 400 si falta procedureTypeId. Requiere SuperAdmin.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // PUT /{id} — actualiza orden/obligatoriedad (AC3 → 200 / 422 / 404).
        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("AdminProcedureDocumentRequirementUpdate")
            .WithSummary("Actualiza orden y obligatoriedad de una asociación")
            .WithDescription("Modifica únicamente el orden default y la obligatoriedad de la asociación. "
                + "404 si no existe, 422 si el payload es inválido. Requiere SuperAdmin.")
            .Produces<ProcedureDocumentRequirementResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // DELETE /{id} — borrado físico (AC4 → 204 / 409 / 404).
        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("AdminProcedureDocumentRequirementDelete")
            .WithSummary("Elimina la asociación documento↔trámite")
            .WithDescription("Borra la asociación. 409 si el documento ya fue usado en trámites creados "
                + "(snapshot inmutable), 404 si no existe. Requiere SuperAdmin.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateProcedureDocumentRequirementRequest request,
        HttpContext httpContext,
        [FromServices] CreateProcedureDocumentRequirementHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateProcedureDocumentRequirementCommand
        {
            Request = request,
            CreatedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            CreateProcedureDocumentRequirementOutcome.Created => Results.Created(
                $"/api/v1/admin/procedure-document-requirements/{result.Response!.Id}", result.Response),
            CreateProcedureDocumentRequirementOutcome.ValidationFailed => Results.Json(
                new ErrorResponse(result.Error!), statusCode: StatusCodes.Status422UnprocessableEntity),
            CreateProcedureDocumentRequirementOutcome.ProcedureTypeNotFound => Results.NotFound(
                new ErrorResponse($"No existe el tipo de trámite {request.ProcedureTypeId}.")),
            _ => Results.NotFound(
                new ErrorResponse($"No existe el tipo de documento {request.DocumentTypeId}.")),
        };
    }

    private static async Task<IResult> ListAsync(
        [FromServices] ListProcedureDocumentRequirementsHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] Guid? procedureTypeId = null)
    {
        if (procedureTypeId is null || procedureTypeId == Guid.Empty)
        {
            return Results.Json(
                new ErrorResponse("El parámetro procedureTypeId es obligatorio."),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await handler
            .HandleAsync(new ListProcedureDocumentRequirementsQuery { ProcedureTypeId = procedureTypeId.Value }, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateProcedureDocumentRequirementRequest request,
        [FromServices] UpdateProcedureDocumentRequirementHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdateProcedureDocumentRequirementCommand
        {
            Id = id,
            Request = request,
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            UpdateProcedureDocumentRequirementOutcome.Updated => Results.Ok(result.Response),
            UpdateProcedureDocumentRequirementOutcome.ValidationFailed => Results.Json(
                new ErrorResponse(result.Error!), statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.NotFound(new ErrorResponse($"No existe la asociación {id}.")),
        };
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        [FromServices] DeleteProcedureDocumentRequirementHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new DeleteProcedureDocumentRequirementCommand { Id = id }, cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            DeleteProcedureDocumentRequirementOutcome.Deleted => Results.NoContent(),
            DeleteProcedureDocumentRequirementOutcome.InUse => Results.Json(
                new ErrorResponse(DeleteProcedureDocumentRequirementResult.InUseMessage),
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.NotFound(new ErrorResponse($"No existe la asociación {id}.")),
        };
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Cuerpo de error simple: <c>{ error: "mensaje" }</c> (400 / 422 / 404 / 409).</summary>
    private sealed record ErrorResponse(string Error);
}
