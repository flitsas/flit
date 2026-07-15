using System.Security.Claims;
using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Application.Companies.ListCompanies;
using Flit.Admin.Application.Companies.SetCompanyStatus;
using Flit.Admin.Application.Companies.UpdateCompany;
using Flit.Admin.Application.Companies.Settings;
using Flit.Admin.Application.Companies.Settings.GetTenantSettings;
using Flit.Admin.Application.Companies.Settings.UpdateTenantSettings;
using Flit.Admin.Application.Companies.TransitOffices;
using Flit.Admin.Application.Companies.TransitOffices.AddTransitGrant;
using Flit.Admin.Application.Companies.TransitOffices.GetOtConsultationRestrictions;
using Flit.Admin.Application.Companies.TransitOffices.GetTenantAuditLog;
using Flit.Admin.Application.Companies.TransitOffices.GetTransitGrants;
using Flit.Admin.Application.Companies.TransitOffices.RemoveTransitGrant;
using Flit.Admin.Application.Companies.TransitOffices.SetOtConsultationRestriction;
using Flit.Admin.Application.Companies.Whitelist;
using Flit.Admin.Application.Companies.Whitelist.AddWhitelistEmails;
using Flit.Admin.Application.Companies.Whitelist.GetWhitelist;
using Flit.Admin.Domain.Companies;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints del Administrador de Compañías (HU #10189, #10190, #10191, #10192).
/// Todo el grupo exige rol SuperAdmin (RF01 / AC5).
/// </summary>
public static class AdminCompaniesEndpoints
{
    public static IEndpointRouteBuilder MapAdminCompaniesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Compañías");

        // GET /api/v1/admin/companies/index — listado paginado con filtros (#10189 AC1, AC2).
        group.MapGet("/index", ListCompaniesAsync)
            .WithName("AdminCompaniesIndex")
            .WithSummary("Lista compañías paginadas")
            .WithDescription("Listado paginado de compañías con filtros opcionales por NIT, razón social, "
                + "estado y rango de fechas de creación. Requiere SuperAdmin.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // POST /api/v1/admin/companies — alta de compañía (botón "Crear compañía", #10118).
        group.MapPost("", CreateCompanyAsync)
            .WithName("AdminCompanyCreate")
            .WithSummary("Crea una compañía")
            .WithDescription("Da de alta una compañía B2B (tenant). 422 con detalle por campo si la validación "
                + "falla (NIT/razón social/duplicados). Requiere SuperAdmin.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // PUT /api/v1/admin/companies/{tenantId} — edición de datos de la compañía (#10118).
        group.MapPut("/{tenantId:guid}", UpdateCompanyAsync)
            .WithName("AdminCompanyUpdate")
            .WithSummary("Edita una compañía")
            .WithDescription("Actualiza razón social, NIT, tipo y estado de la compañía. El código es "
                + "inmutable. 422 con detalle por campo si la validación falla; 404 si la compañía no "
                + "existe; 409 si otra persona la editó (rowVersion desactualizado). Requiere SuperAdmin.")
            .Produces<CompanyListItem>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // PUT /api/v1/admin/companies/{tenantId}/status — activa/desactiva la compañía (#10118).
        group.MapPut("/{tenantId:guid}/status", SetStatusAsync)
            .WithName("AdminCompanySetStatus")
            .WithSummary("Activa o desactiva una compañía")
            .WithDescription("Cambia el estado activo/inactivo de la compañía "
                + "(identity.tenants.is_active). Idempotente; 404 si la compañía no existe. Requiere SuperAdmin.")
            .Produces<CompanyListItem>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // GET /api/v1/admin/companies/{tenantId}/settings — configuración actual (#10190 AC3).
        group.MapGet("/{tenantId:guid}/settings", GetSettingsAsync)
            .WithName("AdminCompanyGetSettings")
            .WithSummary("Obtiene la configuración operativa del tenant")
            .WithDescription("Retorna la configuración operativa de la compañía. 404 si el tenant no tiene "
                + "configuración. Requiere SuperAdmin.")
            .Produces<TenantSettingsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // PUT /api/v1/admin/companies/{tenantId}/settings — guardado atómico + audit (#10190 AC1/AC2).
        group.MapPut("/{tenantId:guid}/settings", UpdateSettingsAsync)
            .WithName("AdminCompanyUpdateSettings")
            .WithSummary("Actualiza la configuración operativa del tenant")
            .WithDescription("Guardado atómico de la configuración operativa con registro de auditoría. "
                + "422 con detalle por campo si la validación falla. Requiere SuperAdmin.")
            .Produces<TenantSettingsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // POST /api/v1/admin/companies/{tenantId}/whitelist — alta masiva + audit (#10191 AC4/AC5).
        group.MapPost("/{tenantId:guid}/whitelist", AddWhitelistAsync)
            .WithName("AdminCompanyAddWhitelist")
            .WithSummary("Agrega correos a la whitelist del tenant")
            .WithDescription("Alta masiva de correos exentos; devuelve los insertados y los omitidos "
                + "(duplicados). 422 con detalle por campo si la validación falla. Requiere SuperAdmin.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // GET /api/v1/admin/companies/{tenantId}/whitelist — lista de correos exentos (#10191 AC6).
        group.MapGet("/{tenantId:guid}/whitelist", GetWhitelistAsync)
            .WithName("AdminCompanyGetWhitelist")
            .WithSummary("Lista los correos de la whitelist del tenant")
            .WithDescription("Retorna los correos exentos configurados para la compañía. Requiere SuperAdmin.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // POST /api/v1/admin/companies/{tenantId}/transit-grants — habilita OT + audit (#10192 AC2).
        group.MapPost("/{tenantId:guid}/transit-grants", AddTransitGrantAsync)
            .WithName("AdminCompanyAddTransitGrant")
            .WithSummary("Habilita un Organismo de Tránsito para el tenant")
            .WithDescription("Concede acceso de la compañía a un OT (idempotente: 201 tanto en alta nueva "
                + "como si el grant ya existía). 422 si la validación falla. Requiere SuperAdmin.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // DELETE /api/v1/admin/companies/{tenantId}/transit-grants/{transitOfficeId} — deshabilita OT (#10192 AC3).
        group.MapDelete("/{tenantId:guid}/transit-grants/{transitOfficeId:guid}", RemoveTransitGrantAsync)
            .WithName("AdminCompanyRemoveTransitGrant")
            .WithSummary("Deshabilita un Organismo de Tránsito del tenant")
            .WithDescription("Revoca el acceso de la compañía a un OT. 204 si se eliminó, 404 si el grant "
                + "no existía. Requiere SuperAdmin.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // GET /api/v1/admin/companies/{tenantId}/transit-grants — OT habilitados del tenant (#10192 AC5).
        group.MapGet("/{tenantId:guid}/transit-grants", GetTransitGrantsAsync)
            .WithName("AdminCompanyGetTransitGrants")
            .WithSummary("Lista los OT habilitados del tenant")
            .WithDescription("Retorna los Organismos de Tránsito habilitados para la compañía. Requiere SuperAdmin.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // GET /api/v1/admin/companies/{tenantId}/audit-log — historial de gobernanza paginado (#10192 AC4).
        group.MapGet("/{tenantId:guid}/audit-log", GetAuditLogAsync)
            .WithName("AdminCompanyGetAuditLog")
            .WithSummary("Lista el historial de auditoría del tenant")
            .WithDescription("Historial de gobernanza (cambios de settings, whitelist y grants) paginado. "
                + "Requiere SuperAdmin.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // GET /api/v1/admin/companies/{tenantId}/ot-consultation-restrictions — restricciones
        // de consulta (RNMC, comparendos) por OT de la compañía (HU #10759 AC1/AC5).
        group.MapGet("/{tenantId:guid}/ot-consultation-restrictions", GetOtConsultationRestrictionsAsync)
            .WithName("AdminCompanyGetOtConsultationRestrictions")
            .WithSummary("Lista las restricciones de consulta por OT del tenant")
            .WithDescription("Retorna las filas de restricción configuradas explícitamente (tabla dispersa: "
                + "ausencia de fila = consulta permitida). Requiere SuperAdmin.")
            .Produces<IReadOnlyList<OtConsultationRestrictionResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // PUT /api/v1/admin/companies/{tenantId}/ot-consultation-restrictions/{transitOfficeId}/{consultationKind}
        // — fija el estado deseado (habilitada/inhabilitada) de una consulta por OT (HU #10759 AC1–AC4).
        // PUT (no POST/DELETE): transporta el estado deseado ⇒ idempotente en ambos sentidos, sin 404.
        group.MapPut(
                "/{tenantId:guid}/ot-consultation-restrictions/{transitOfficeId:guid}/{consultationKind}",
                SetOtConsultationRestrictionAsync)
            .WithName("AdminCompanySetOtConsultationRestriction")
            .WithSummary("Fija el estado de una restricción de consulta por OT")
            .WithDescription("Habilita o inhabilita una consulta (rnmc|fines) para un Organismo de Tránsito "
                + "puntual de la compañía. Idempotente: reenviar el mismo estado no duplica auditoría. 422 si "
                + "el OT no existe, no está habilitado para la compañía, o el tipo de consulta no es "
                + "restringible. Requiere SuperAdmin.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> ListCompaniesAsync(
        [FromServices] ListCompaniesHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] string? nit = null,
        [FromQuery] string? razonSocial = null,
        [FromQuery] bool? estadoActivo = null,
        [FromQuery] DateOnly? fechaDesde = null,
        [FromQuery] DateOnly? fechaHasta = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var query = new ListCompaniesQuery
        {
            Nit = nit,
            RazonSocial = razonSocial,
            EstadoActivo = estadoActivo,
            FechaDesde = fechaDesde,
            FechaHasta = fechaHasta,
            Page = page,
            PageSize = pageSize,
        };

        var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> CreateCompanyAsync(
        CreateCompanyRequest request,
        HttpContext httpContext,
        [FromServices] CreateCompanyHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateCompanyCommand
        {
            Request = request,
            CreatedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsValid
            ? Results.Created($"/api/v1/admin/companies/{result.Company!.Id}", result.Company)
            : Results.Json(
                new CompanyValidationErrorResponse(result.Errors),
                statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> UpdateCompanyAsync(
        Guid tenantId,
        UpdateCompanyRequest request,
        HttpContext httpContext,
        [FromServices] UpdateCompanyHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdateCompanyCommand
        {
            TenantId = tenantId,
            Request = request,
            ChangedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            UpdateCompanyOutcome.Updated => Results.Ok(result.Company),
            UpdateCompanyOutcome.NotFound => Results.NotFound(
                new { error = $"No existe la compañía {tenantId}." }),
            UpdateCompanyOutcome.Conflict => Results.Json(
                new { error = "La compañía fue modificada por otra persona. Recarga e intenta de nuevo." },
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(
                new CompanyValidationErrorResponse(result.Errors),
                statusCode: StatusCodes.Status422UnprocessableEntity),
        };
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
            _ => Results.NotFound(new { error = $"No existe la compañía {tenantId}." }),
        };
    }

    private static async Task<IResult> GetSettingsAsync(
        Guid tenantId,
        [FromServices] GetTenantSettingsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new GetTenantSettingsQuery { TenantId = tenantId }, cancellationToken)
            .ConfigureAwait(false);

        return result is null
            ? Results.NotFound(new { error = $"No existe configuración operativa para el tenant {tenantId}." })
            : Results.Ok(result);
    }

    private static async Task<IResult> UpdateSettingsAsync(
        Guid tenantId,
        UpdateTenantSettingsRequest request,
        HttpContext httpContext,
        [FromServices] UpdateTenantSettingsHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdateTenantSettingsCommand
        {
            TenantId = tenantId,
            Request = request,
            ChangedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsValid
            ? Results.Ok(result.Settings)
            : Results.Json(
                new ValidationErrorResponse(result.Errors),
                statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> AddWhitelistAsync(
        Guid tenantId,
        AddWhitelistEmailsRequest request,
        HttpContext httpContext,
        [FromServices] AddWhitelistEmailsHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new AddWhitelistEmailsCommand
        {
            TenantId = tenantId,
            Request = request,
            AddedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsValid
            ? Results.Created(
                $"/api/v1/admin/companies/{tenantId}/whitelist",
                new WhitelistAddResponse(result.AddedEmails, result.SkippedEmails))
            : Results.Json(
                new WhitelistValidationErrorResponse(result.Errors),
                statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> GetWhitelistAsync(
        Guid tenantId,
        [FromServices] GetWhitelistHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new GetWhitelistQuery { TenantId = tenantId }, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> AddTransitGrantAsync(
        Guid tenantId,
        AddTransitGrantRequest request,
        HttpContext httpContext,
        [FromServices] AddTransitGrantHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new AddTransitGrantCommand
        {
            TenantId = tenantId,
            TransitOfficeId = request?.TransitOfficeId ?? Guid.Empty,
            CreatedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        // AC2: 201 tanto en alta nueva como idempotente (grant ya existente, sin duplicar).
        return result.IsValid
            ? Results.Created(
                $"/api/v1/admin/companies/{tenantId}/transit-grants/{command.TransitOfficeId}",
                new TransitGrantCreatedResponse(command.TransitOfficeId, result.Added))
            : Results.Json(
                new TransitGrantValidationErrorResponse(result.Errors),
                statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> RemoveTransitGrantAsync(
        Guid tenantId,
        Guid transitOfficeId,
        HttpContext httpContext,
        [FromServices] RemoveTransitGrantHandler handler,
        CancellationToken cancellationToken)
    {
        var removed = await handler
            .HandleAsync(
                new RemoveTransitGrantCommand
                {
                    TenantId = tenantId,
                    TransitOfficeId = transitOfficeId,
                    ChangedBy = ResolveUserId(httpContext.User),
                },
                cancellationToken)
            .ConfigureAwait(false);

        // AC3: 204 si se eliminó; 404 si el grant no existía.
        return removed
            ? Results.NoContent()
            : Results.NotFound(new { error = $"No existe grant {transitOfficeId} para el tenant {tenantId}." });
    }

    private static async Task<IResult> GetTransitGrantsAsync(
        Guid tenantId,
        [FromServices] GetTransitGrantsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new GetTransitGrantsQuery { TenantId = tenantId }, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetAuditLogAsync(
        Guid tenantId,
        [FromServices] GetTenantAuditLogHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var result = await handler
            .HandleAsync(
                new GetTenantAuditLogQuery { TenantId = tenantId, Page = page, PageSize = pageSize },
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetOtConsultationRestrictionsAsync(
        Guid tenantId,
        [FromServices] GetOtConsultationRestrictionsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new GetOtConsultationRestrictionsQuery { TenantId = tenantId }, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> SetOtConsultationRestrictionAsync(
        Guid tenantId,
        Guid transitOfficeId,
        string consultationKind,
        SetOtConsultationRestrictionRequest request,
        HttpContext httpContext,
        [FromServices] SetOtConsultationRestrictionHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new SetOtConsultationRestrictionCommand
        {
            TenantId = tenantId,
            TransitOfficeId = transitOfficeId,
            ConsultationKind = consultationKind,
            Enabled = request.Enabled,
            ChangedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        // AC1/AC2: 204 tanto en alta nueva como en no-op idempotente (mismo estado deseado).
        return result.IsValid
            ? Results.NoContent()
            : Results.Json(
                new OtConsultationRestrictionValidationErrorResponse(result.Errors),
                statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Cuerpo de error de validación 422: <c>{ errors: [{ field, message }] }</c>.</summary>
    private sealed record ValidationErrorResponse(IReadOnlyList<SettingsValidationError> Errors);

    /// <summary>Cuerpo de error 422 del alta de compañía: <c>{ errors: [{ field, message }] }</c>.</summary>
    private sealed record CompanyValidationErrorResponse(IReadOnlyList<CompanyValidationError> Errors);

    /// <summary>Cuerpo 201 del alta de whitelist: correos insertados y omitidos (duplicados).</summary>
    private sealed record WhitelistAddResponse(IReadOnlyList<string> Added, IReadOnlyList<string> Skipped);

    /// <summary>Cuerpo de error 422 de whitelist: <c>{ errors: [{ field, message, value }] }</c>.</summary>
    private sealed record WhitelistValidationErrorResponse(IReadOnlyList<WhitelistValidationError> Errors);

    /// <summary>Cuerpo 201 del alta de grant: id del OT y si fue alta nueva (o idempotente).</summary>
    private sealed record TransitGrantCreatedResponse(Guid TransitOfficeId, bool Created);

    /// <summary>Cuerpo de error 422 de grant: <c>{ errors: [{ field, message, value }] }</c>.</summary>
    private sealed record TransitGrantValidationErrorResponse(IReadOnlyList<TransitGrantValidationError> Errors);

    /// <summary>Cuerpo de error 422 de restricción de consulta: <c>{ errors: [{ field, message, value }] }</c>.</summary>
    private sealed record OtConsultationRestrictionValidationErrorResponse(
        IReadOnlyList<OtConsultationRestrictionValidationError> Errors);
}
