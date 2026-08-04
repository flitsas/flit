using System.Security.Claims;
using Flit.Admin.Application.Companies.Deeds;
using Flit.Admin.Application.Companies.Deeds.CreateDeed;
using Flit.Admin.Application.Companies.Deeds.DeleteDeed;
using Flit.Admin.Application.Companies.Deeds.GetDeed;
using Flit.Admin.Application.Companies.Deeds.ListDeeds;
using Flit.Admin.Application.Companies.Deeds.UpdateDeed;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de gestión de escrituras (PDF) por compañía (HU #10902, ADR-0033). Módulo Admin
/// Compañías: <see cref="AdminAuthorization.AdminCompanyPolicy"/> +
/// <see cref="CompanyOwnTenantFilter"/> (paridad HU #11228 con RL/baúl; AdminCompany solo su
/// tenant, SuperAdmin bypass). El PDF viaja DIRECTO a S3 vía presigned URLs (nunca por el request
/// del API ni en BD); las respuestas exponen solo metadatos + la referencia al artefacto, y el PDF
/// se ve con una presigned URL de vida corta. Las validaciones responden 422 con el sobre
/// <c>{ errors: [{ field, code, message }] }</c>.
/// </summary>
public static class AdminDeedsEndpoints
{
    public static IEndpointRouteBuilder MapAdminDeedsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies/{tenantId:guid}/deeds")
            .RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
            .AddEndpointFilter<CompanyOwnTenantFilter>()
            .WithTags("Admin · Escrituras");

        // GET — listado paginado de escrituras del tenant (envelope { data, totalCount, page, pageSize }).
        group.MapGet("", ListAsync)
            .WithName("AdminDeedsList")
            .WithSummary("Lista paginada las escrituras de una compañía")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // GET /{id} — una escritura por id + presigned URL de vista inline del PDF.
        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("AdminDeedsGetById")
            .WithSummary("Obtiene una escritura por id con URL presignada de vista")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // POST — alta de escritura (registra el documento en storage y devuelve la presigned upload).
        group.MapPost("", CreateAsync)
            .WithName("AdminDeedsCreate")
            .WithSummary("Registra una escritura (PDF) con vigencia y compañías")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // PUT /{id} — edición de metadatos (+ reemplazo opcional del PDF).
        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("AdminDeedsUpdate")
            .WithSummary("Edita una escritura (descripción, vigencia, compañías, PDF opcional)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // DELETE /{id} — baja lógica idempotente.
        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("AdminDeedsDelete")
            .WithSummary("Da de baja una escritura (idempotente)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid tenantId,
        [FromServices] ListDeedsHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await handler
            .HandleAsync(
                new ListDeedsQuery { TenantId = tenantId, Page = page, PageSize = pageSize },
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new
        {
            data = result.Data,
            totalCount = result.TotalCount,
            page = result.Page,
            pageSize = result.PageSize,
        });
    }

    private static async Task<IResult> GetByIdAsync(
        Guid tenantId,
        Guid id,
        [FromServices] GetDeedByIdHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new GetDeedByIdQuery { TenantId = tenantId, Id = id }, cancellationToken)
            .ConfigureAwait(false);

        return result is null
            ? Results.NotFound(new { error = $"No existe la escritura {id} en esta compañía." })
            : Results.Ok(result);
    }

    private static async Task<IResult> CreateAsync(
        Guid tenantId,
        CreateDeedRequest request,
        HttpContext httpContext,
        [FromServices] CreateDeedHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler
            .HandleAsync(
                new CreateDeedCommand
                {
                    TenantId = tenantId,
                    Description = request.Description,
                    VigenciaDesde = request.VigenciaDesde,
                    VigenciaHasta = request.VigenciaHasta,
                    Sha256 = request.Sha256,
                    RepresentedCompanyIds = request.RepresentedCompanyIds,
                    RepresentativeId = request.RepresentativeId,
                    CreatedBy = ResolveUserId(httpContext.User),
                },
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsValid
            ? Results.Created(
                $"/api/v1/admin/companies/{tenantId}/deeds/{result.DeedId}",
                new { id = result.DeedId, upload = ToUpload(result.Upload!) })
            : ValidationProblem(result.Errors);
    }

    private static async Task<IResult> UpdateAsync(
        Guid tenantId,
        Guid id,
        UpdateDeedRequest request,
        HttpContext httpContext,
        [FromServices] UpdateDeedHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler
            .HandleAsync(
                new UpdateDeedCommand
                {
                    TenantId = tenantId,
                    Id = id,
                    Description = request.Description,
                    VigenciaDesde = request.VigenciaDesde,
                    VigenciaHasta = request.VigenciaHasta,
                    Sha256 = request.Sha256,
                    RepresentedCompanyIds = request.RepresentedCompanyIds,
                    UpdatedBy = ResolveUserId(httpContext.User),
                },
                cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            UpdateDeedOutcome.Updated => Results.Ok(new
            {
                id,
                upload = result.Upload is null ? null : ToUpload(result.Upload),
            }),
            UpdateDeedOutcome.NotFound => Results.NotFound(
                new { error = $"No existe la escritura {id} en esta compañía." }),
            _ => ValidationProblem(result.Errors),
        };
    }

    private static async Task<IResult> DeleteAsync(
        Guid tenantId,
        Guid id,
        HttpContext httpContext,
        [FromServices] DeleteDeedHandler handler,
        CancellationToken cancellationToken)
    {
        var outcome = await handler
            .HandleAsync(
                new DeleteDeedCommand
                {
                    TenantId = tenantId,
                    Id = id,
                    ChangedBy = ResolveUserId(httpContext.User),
                },
                cancellationToken)
            .ConfigureAwait(false);

        return outcome == DeleteDeedOutcome.Deleted
            ? Results.NoContent()
            : Results.NotFound(new { error = $"No existe la escritura {id} en esta compañía." });
    }

    private static object ToUpload(DeedUploadTicket ticket) =>
        new { storagePath = ticket.StoragePath, url = ticket.Url, fields = ticket.Fields };

    /// <summary>422 con el sobre estándar de errores; nunca incluye PII.</summary>
    private static IResult ValidationProblem(IReadOnlyList<DeedValidationError> errors) =>
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
