using System.Security.Claims;
using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.InactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.ListMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.ListOtCompanies;
using Flit.Admin.Application.Companies.MandateSigners.ReactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de mandatarios (firmantes de mandato) por organismo de tránsito (ADR-0023,
/// RF22–RF28, RF33, RF34). Módulo Admin OT: SuperAdmin u ot_admin (<see cref="AdminAuthorization.OtModulePolicy"/>).
/// El número de documento es PII (Ley 1581): nunca se escribe en logs ni en mensajes de error.
/// </summary>
public static class AdminMandateSignersEndpoints
{
    public static IEndpointRouteBuilder MapAdminMandateSignersEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/transit-offices/{transitOfficeId:guid}/mandate-signers")
            .RequireAuthorization(AdminAuthorization.OtModulePolicy)
            .WithTags("Admin · Mandatarios");

        // GET — mandatarios activos del OT con sus compañías (RF27).
        group.MapGet("", ListAsync)
            .WithName("AdminMandateSignersList")
            .WithSummary("Lista los mandatarios activos de un organismo de tránsito")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // GET /companies — compañías del OT con su mandatario resuelto (RF34 + multiselect).
        group.MapGet("/companies", ListCompaniesAsync)
            .WithName("AdminMandateSignersCompanies")
            .WithSummary("Lista las compañías del OT con su mandatario asignado (vista consolidada)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // POST — alta de mandatario (RF22).
        group.MapPost("", CreateAsync)
            .WithName("AdminMandateSignersCreate")
            .WithSummary("Registra un mandatario en el organismo de tránsito")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // PUT /{signerId} — edición (RF23, regenera huella).
        group.MapPut("/{mandateSignerId:guid}", UpdateAsync)
            .WithName("AdminMandateSignersUpdate")
            .WithSummary("Edita un mandatario (regenera la huella de integridad)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // POST /{signerId}/inactivate — baja lógica que libera compañías (RF24).
        group.MapPost("/{mandateSignerId:guid}/inactivate", InactivateAsync)
            .WithName("AdminMandateSignersInactivate")
            .WithSummary("Inactiva un mandatario y libera sus compañías")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // POST /{signerId}/reactivate — reactiva un mandatario inactivado (sin compañías).
        group.MapPost("/{mandateSignerId:guid}/reactivate", ReactivateAsync)
            .WithName("AdminMandateSignersReactivate")
            .WithSummary("Reactiva un mandatario inactivado (se reasignan sus compañías)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid transitOfficeId,
        [FromServices] ListMandateSignersHandler handler,
        [FromServices] Flit.Admin.Application.Identity.AdminIdentityMockOptions mockOptions,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new ListMandateSignersQuery { TransitOfficeId = transitOfficeId }, cancellationToken)
            .ConfigureAwait(false);

        // HU #11028 — la consola solo ofrece "Simular validación" si el ambiente la tiene habilitada.
        return Results.Ok(new { data = result, mockIdentityEnabled = mockOptions.Enabled });
    }

    private static async Task<IResult> ListCompaniesAsync(
        Guid transitOfficeId,
        [FromServices] ListOtCompaniesHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new ListOtCompaniesQuery { TransitOfficeId = transitOfficeId }, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new { data = result });
    }

    private static async Task<IResult> CreateAsync(
        Guid transitOfficeId,
        CreateMandateSignerRequest request,
        HttpContext httpContext,
        [FromServices] CreateMandateSignerHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateMandateSignerCommand
        {
            TransitOfficeId = transitOfficeId,
            FullName = request.FullName ?? string.Empty,
            DocumentNumber = request.DocumentNumber ?? string.Empty,
            CompanyTenantIds = request.CompanyTenantIds ?? [],
            DocumentType = request.DocumentType ?? "CC",
            Email = request.Email,
            UserId = request.UserId,
            // HU #11201 — la misma persona puede firmar en varios organismos.
            TransitOfficeIds = request.TransitOfficeIds,
            CreatedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsValid
            ? Results.Created(
                $"/api/v1/admin/transit-offices/{transitOfficeId}/mandate-signers/{result.MandateSignerId}",
                new
                {
                    id = result.MandateSignerId,
                    integrityHash = result.IntegrityHash,
                    // HU #11000 — desenlace de la validación de identidad disparada por el alta, para que
                    // el aviso al usuario sea veraz ("enviada" / "ya validada" / "no se pudo enviar").
                    identity = result.Identity.ToString().ToLowerInvariant(),
                })
            : ValidationProblem(result.Errors);
    }

    private static async Task<IResult> UpdateAsync(
        Guid transitOfficeId,
        Guid mandateSignerId,
        UpdateMandateSignerRequest request,
        HttpContext httpContext,
        [FromServices] UpdateMandateSignerHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdateMandateSignerCommand
        {
            TransitOfficeId = transitOfficeId,
            MandateSignerId = mandateSignerId,
            FullName = request.FullName ?? string.Empty,
            DocumentNumber = request.DocumentNumber ?? string.Empty,
            CompanyTenantIds = request.CompanyTenantIds ?? [],
            DocumentType = request.DocumentType ?? "CC",
            Email = request.Email,
            UserId = request.UserId,
            TransitOfficeIds = request.TransitOfficeIds,
            UpdatedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            UpdateMandateSignerOutcome.Updated =>
                Results.Ok(new { id = mandateSignerId, integrityHash = result.IntegrityHash }),
            UpdateMandateSignerOutcome.NotFound =>
                Results.NotFound(new { error = $"No existe el mandatario {mandateSignerId} en este organismo." }),
            _ => ValidationProblem(result.Errors),
        };
    }

    private static async Task<IResult> InactivateAsync(
        Guid transitOfficeId,
        Guid mandateSignerId,
        HttpContext httpContext,
        [FromServices] InactivateMandateSignerHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new InactivateMandateSignerCommand
        {
            TransitOfficeId = transitOfficeId,
            MandateSignerId = mandateSignerId,
            ChangedBy = ResolveUserId(httpContext.User),
        };

        var outcome = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return outcome == InactivateMandateSignerOutcome.Inactivated
            ? Results.NoContent()
            : Results.NotFound(new { error = $"No existe el mandatario {mandateSignerId} en este organismo." });
    }

    private static async Task<IResult> ReactivateAsync(
        Guid transitOfficeId,
        Guid mandateSignerId,
        HttpContext httpContext,
        [FromServices] ReactivateMandateSignerHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new ReactivateMandateSignerCommand
        {
            TransitOfficeId = transitOfficeId,
            MandateSignerId = mandateSignerId,
            ChangedBy = ResolveUserId(httpContext.User),
        };

        var outcome = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return outcome == ReactivateMandateSignerOutcome.Reactivated
            ? Results.NoContent()
            : Results.NotFound(new { error = $"No existe el mandatario {mandateSignerId} en este organismo." });
    }

    /// <summary>422 con el sobre estándar de errores; nunca incluye PII.</summary>
    private static IResult ValidationProblem(
        IReadOnlyList<Flit.Admin.Application.Companies.MandateSigners.MandateSignerValidationError> errors) =>
        Results.Json(
            new { errors = errors.Select(e => new { field = e.Field, message = e.Message, value = e.Value }) },
            statusCode: StatusCodes.Status422UnprocessableEntity);

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
