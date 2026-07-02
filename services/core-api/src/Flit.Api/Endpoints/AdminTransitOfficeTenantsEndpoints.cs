using System.Security.Claims;
using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Application.Companies.SetCompanyStatus;
using Flit.Admin.Application.Companies.TransitOffices.CreateTransitOffice;
using Flit.Admin.Application.Companies.TransitOffices.ListTransitOfficeTenants;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de alta/listado de tenants Organismo de Tránsito (OT) — refactor
/// "adminOT" (completar el OT como tenant de primera clase). El alta es exclusiva de
/// SuperAdmin (a diferencia de <c>/api/v1/admin/ot/*</c>, que además permite
/// <c>ot_admin</c> para self-service dentro de su propio tenant).
/// </summary>
public static class AdminTransitOfficeTenantsEndpoints
{
    public static IEndpointRouteBuilder MapAdminTransitOfficeTenantsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/transit-office-tenants")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Tenants OT");

        // GET /api/v1/admin/transit-office-tenants/index — listado paginado.
        group.MapGet("/index", ListTransitOfficeTenantsAsync)
            .WithName("AdminTransitOfficeTenantsIndex")
            .WithSummary("Lista tenants OT paginados")
            .WithDescription("Listado paginado de tenants Organismo de Tránsito (join identity.tenants + "
                + "admin.transit_office_profiles), con filtros opcionales por razón social y estado. "
                + "Requiere SuperAdmin.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // POST /api/v1/admin/transit-office-tenants — alta de tenant OT.
        group.MapPost("", CreateTransitOfficeAsync)
            .WithName("AdminTransitOfficeTenantCreate")
            .WithSummary("Crea un tenant OT")
            .WithDescription("Da de alta un tenant Organismo de Tránsito: tenant (tenant_type=RENTING), rol "
                + "de sistema ot_admin sin permisos (el SuperAdmin los cura después vía RBAC Admin) y perfil "
                + "OT vinculado a la oficina del catálogo. 422 con detalle por campo si la validación falla "
                + "(incluye el caso de oficina ya asignada a otro tenant OT). Requiere SuperAdmin.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // PUT /api/v1/admin/transit-office-tenants/{tenantId}/status — activa/desactiva el tenant OT.
        // Reutiliza directamente SetCompanyStatusHandler: es genérico sobre identity.tenants y no
        // le importa el tenant_type.
        group.MapPut("/{tenantId:guid}/status", SetStatusAsync)
            .WithName("AdminTransitOfficeTenantSetStatus")
            .WithSummary("Activa o desactiva un tenant OT")
            .WithDescription("Cambia el estado activo/inactivo del tenant OT (identity.tenants.is_active). "
                + "Idempotente; 404 si el tenant no existe. Requiere SuperAdmin.")
            .Produces<Flit.Admin.Domain.Companies.CompanyListItem>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListTransitOfficeTenantsAsync(
        [FromServices] ListTransitOfficeTenantsHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] string? legalName = null,
        [FromQuery] bool? estadoActivo = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var query = new ListTransitOfficeTenantsQuery
        {
            LegalName = legalName,
            EstadoActivo = estadoActivo,
            Page = page,
            PageSize = pageSize,
        };

        var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> CreateTransitOfficeAsync(
        CreateTransitOfficeRequest request,
        HttpContext httpContext,
        [FromServices] CreateTransitOfficeHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateTransitOfficeCommand
        {
            Request = request,
            CreatedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsValid
            ? Results.Created(
                $"/api/v1/admin/transit-office-tenants/{result.TransitOffice!.Id}",
                result.TransitOffice)
            : Results.Json(
                new CompanyValidationErrorResponse(result.Errors),
                statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> SetStatusAsync(
        Guid tenantId,
        SetCompanyStatusRequest request,
        HttpContext httpContext,
        [FromServices] SetCompanyStatusHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(
                new SetCompanyStatusCommand
                {
                    TenantId = tenantId,
                    EstadoActivo = request.EstadoActivo,
                    ChangedBy = ResolveUserId(httpContext.User),
                },
                cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            SetCompanyStatusOutcome.Updated => Results.Ok(result.Company),
            _ => Results.NotFound(new { error = $"No existe el tenant {tenantId}." }),
        };
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Cuerpo de error 422 del alta de tenant OT: <c>{ errors: [{ field, message }] }</c>.</summary>
    private sealed record CompanyValidationErrorResponse(IReadOnlyList<CompanyValidationError> Errors);
}
