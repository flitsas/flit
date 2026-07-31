using System.Security.Claims;
using Flit.Admin.Application.Companies.LegalRepresentatives;
using Flit.Admin.Application.Companies.LegalRepresentatives.CreateLegalRepresentative;
using Flit.Admin.Application.Companies.LegalRepresentatives.DeleteLegalRepresentative;
using Flit.Admin.Application.Companies.LegalRepresentatives.GetLegalRepresentative;
using Flit.Admin.Application.Companies.LegalRepresentatives.ListLegalRepresentatives;
using Flit.Admin.Application.Companies.LegalRepresentatives.UpdateLegalRepresentative;
using Flit.Admin.Domain.DocumentRequirements;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints del directorio de representantes legales por compañía (ADR-0033, HU #10901). Módulo Admin
/// Compañías: solo SuperAdmin (<see cref="AdminAuthorization.SuperAdminPolicy"/>). Al guardar se resuelve
/// la firma del baúl o la validación de identidad vigente; si no hay ninguna, la respuesta incluye la
/// señal <c>sin_firma_ni_identidad</c> (el guardado igual persiste). El número de documento es PII (Ley
/// 1581): nunca se escribe en logs ni en mensajes de error.
/// </summary>
public static class AdminLegalRepresentativesEndpoints
{
    public static IEndpointRouteBuilder MapAdminLegalRepresentativesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies/{tenantId:guid}/legal-representatives")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Representantes Legales");

        // GET — listado paginado de representantes del tenant ({ data, totalCount, page, pageSize }).
        group.MapGet("", ListAsync)
            .WithName("AdminLegalRepresentativesList")
            .WithSummary("Lista paginada de representantes legales de una compañía")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // GET /{id} — un representante por id.
        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("AdminLegalRepresentativesGetById")
            .WithSummary("Obtiene un representante legal por id")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // POST — alta (201 con id + señales; 422 si faltan datos o un tipo de trámite no existe).
        group.MapPost("", CreateAsync)
            .WithName("AdminLegalRepresentativesCreate")
            .WithSummary("Crea un representante legal de la compañía")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // PUT /{id} — edición (200 con id + señales; 404 si no existe; 422 si inválido).
        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("AdminLegalRepresentativesUpdate")
            .WithSummary("Edita un representante legal de la compañía")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // DELETE /{id} — baja lógica idempotente.
        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("AdminLegalRepresentativesDelete")
            .WithSummary("Desactiva (baja lógica) un representante legal")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // GET /procedure-types — catálogo de tipos de trámite asignables (activos + publicados).
        // Alimenta el multiselect del formulario con los IDs REALES del catálogo: los seeds usan
        // uuidv7() (ids no deterministas por BD), así que el FE no puede fijarlos hardcodeados
        // (corrige el 422 `tipo_tramite_inexistente`). Catálogo global; el tenantId de la ruta no
        // filtra (procedure_types no es tenant-scoped), pero mantiene la pantalla bajo el mismo grupo.
        group.MapGet("/procedure-types", ListProcedureTypesAsync)
            .WithName("AdminLegalRepresentativesProcedureTypes")
            .WithSummary("Tipos de trámite asignables (activos y publicados)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid tenantId,
        [FromServices] ListLegalRepresentativesHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var result = await handler
            .HandleAsync(
                new ListLegalRepresentativesQuery { TenantId = tenantId, Page = page, PageSize = pageSize },
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid tenantId,
        Guid id,
        [FromServices] GetLegalRepresentativeByIdHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new GetLegalRepresentativeByIdQuery { TenantId = tenantId, Id = id }, cancellationToken)
            .ConfigureAwait(false);

        return result is null
            ? Results.NotFound(new { error = $"No existe el representante {id} en esta compañía." })
            : Results.Ok(result);
    }

    private static async Task<IResult> CreateAsync(
        Guid tenantId,
        CreateLegalRepresentativeRequest request,
        HttpContext httpContext,
        [FromServices] CreateLegalRepresentativeHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new CreateLegalRepresentativeCommand
        {
            TenantId = tenantId,
            CompanyNit = request.CompanyNit,
            CompanyName = request.CompanyName,
            CompanyEmail = request.CompanyEmail,
            CompanyAddress = request.CompanyAddress,
            CompanyCity = request.CompanyCity,
            CompanyPhone = request.CompanyPhone,
            DocumentType = request.DocumentType,
            DocumentNumber = request.DocumentNumber,
            FirstLastName = request.FirstLastName,
            SecondLastName = request.SecondLastName,
            Name = request.Name,
            Email = request.Email,
            Address = request.Address,
            City = request.City,
            Phone = request.Phone,
            ProcedureTypeIds = request.ProcedureTypeIds ?? [],
            Companies = request.Companies,
            SignatureVaultId = request.SignatureVaultId,
            ActorBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        if (!result.IsValid)
        {
            return ValidationProblem(result.Errors);
        }

        return Results.Created(
            $"/api/v1/admin/companies/{tenantId}/legal-representatives/{result.Id}",
            new { id = result.Id, signals = result.Signals });
    }

    private static async Task<IResult> UpdateAsync(
        Guid tenantId,
        Guid id,
        UpdateLegalRepresentativeRequest request,
        HttpContext httpContext,
        [FromServices] UpdateLegalRepresentativeHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new UpdateLegalRepresentativeCommand
        {
            TenantId = tenantId,
            Id = id,
            CompanyNit = request.CompanyNit,
            CompanyName = request.CompanyName,
            CompanyEmail = request.CompanyEmail,
            CompanyAddress = request.CompanyAddress,
            CompanyCity = request.CompanyCity,
            CompanyPhone = request.CompanyPhone,
            DocumentType = request.DocumentType,
            DocumentNumber = request.DocumentNumber,
            FirstLastName = request.FirstLastName,
            SecondLastName = request.SecondLastName,
            Name = request.Name,
            Email = request.Email,
            Address = request.Address,
            City = request.City,
            Phone = request.Phone,
            ProcedureTypeIds = request.ProcedureTypeIds ?? [],
            Companies = request.Companies,
            SignatureVaultId = request.SignatureVaultId,
            ActorBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        if (result.NotFound)
        {
            return Results.NotFound(new { error = $"No existe el representante {id} en esta compañía." });
        }

        return result.IsValid
            ? Results.Ok(new { id = result.Id, signals = result.Signals })
            : ValidationProblem(result.Errors);
    }

    private static async Task<IResult> DeleteAsync(
        Guid tenantId,
        Guid id,
        HttpContext httpContext,
        [FromServices] DeleteLegalRepresentativeHandler handler,
        CancellationToken cancellationToken)
    {
        var outcome = await handler
            .HandleAsync(
                new DeleteLegalRepresentativeCommand
                {
                    TenantId = tenantId,
                    Id = id,
                    ChangedBy = ResolveUserId(httpContext.User),
                },
                cancellationToken)
            .ConfigureAwait(false);

        return outcome == DeleteLegalRepresentativeOutcome.Deactivated
            ? Results.NoContent()
            : Results.NotFound(new { error = $"No existe el representante {id} en esta compañía." });
    }

    private static async Task<IResult> ListProcedureTypesAsync(
        Guid tenantId,
        [FromServices] IProcedureTypeCatalog catalog,
        CancellationToken cancellationToken)
    {
        var items = await catalog.ListActivePublishedAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(items.Select(p => new { id = p.Id, code = p.Code, name = p.Name }));
    }

    /// <summary>422 con el sobre estándar de errores; nunca incluye PII.</summary>
    private static IResult ValidationProblem(IReadOnlyList<LegalRepresentativeValidationError> errors) =>
        Results.Json(
            new { errors = errors.Select(e => new { field = e.Field, code = e.Code, message = e.Message }) },
            statusCode: StatusCodes.Status422UnprocessableEntity);

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
