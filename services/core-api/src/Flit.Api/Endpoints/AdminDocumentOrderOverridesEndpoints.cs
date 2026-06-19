using System.Security.Claims;
using Flit.Admin.Application.DocumentOrderOverrides.CreateDocumentOrderOverride;
using Flit.Admin.Application.DocumentOrderOverrides.DeleteDocumentOrderOverride;
using Flit.Admin.Application.DocumentOrderOverrides.ListDocumentOrderOverrides;
using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de los overrides de orden documental por OT/Cliente (HU #10196).
/// Todo el grupo exige rol SuperAdmin. El ámbito (<c>scope=OT|CLIENTE</c>) viaja como
/// query; la referencia se toma de <c>transitOfficeId</c> o <c>clienteId</c> según el scope.
/// </summary>
public static class AdminDocumentOrderOverridesEndpoints
{
    public static IEndpointRouteBuilder MapAdminDocumentOrderOverridesEndpoints(
        this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/document-order-overrides")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy);

        // POST ?scope=OT|CLIENTE — crea el override (AC1/AC2 → 201 / 422 / 404 / 400).
        group.MapPost("/", CreateAsync).WithName("AdminDocumentOrderOverrideCreate");

        // GET ?procedureTypeId&scope&transitOfficeId|clienteId — lista por scope (AC5 → 200 / 400).
        group.MapGet("/", ListAsync).WithName("AdminDocumentOrderOverrideList");

        // DELETE /{id} — borrado físico (AC6 → 204 / 404).
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("AdminDocumentOrderOverrideDelete");

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
                new ErrorResponse("El parámetro scope es obligatorio y debe ser OT o CLIENTE."),
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
            _ => Results.NotFound(new ErrorResponse(normalizedScope == DocumentOrderScope.Ot
                ? $"No existe el organismo de tránsito {request.TransitOfficeId}."
                : $"No existe el cliente {request.ClienteId}.")),
        };
    }

    private static async Task<IResult> ListAsync(
        [FromServices] ListDocumentOrderOverridesHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] Guid? procedureTypeId = null,
        [FromQuery] string? scope = null,
        [FromQuery] Guid? transitOfficeId = null,
        [FromQuery] Guid? clienteId = null)
    {
        if (procedureTypeId is null || procedureTypeId == Guid.Empty)
        {
            return BadRequest("El parámetro procedureTypeId es obligatorio.");
        }

        var normalizedScope = DocumentOrderScope.Normalize(scope);
        if (normalizedScope is null)
        {
            return BadRequest("El parámetro scope es obligatorio y debe ser OT o CLIENTE.");
        }

        Guid scopeRefId;
        if (normalizedScope == DocumentOrderScope.Ot)
        {
            if (transitOfficeId is null || transitOfficeId == Guid.Empty)
            {
                return BadRequest("El parámetro transitOfficeId es obligatorio para scope OT.");
            }

            scopeRefId = transitOfficeId.Value;
        }
        else
        {
            if (clienteId is null || clienteId == Guid.Empty)
            {
                return BadRequest("El parámetro clienteId es obligatorio para scope CLIENTE.");
            }

            scopeRefId = clienteId.Value;
        }

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
