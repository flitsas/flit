using System.Security.Claims;
using Flit.Admin.Application.Companies.ListCompanies;
using Flit.Admin.Application.Companies.Settings;
using Flit.Admin.Application.Companies.Settings.GetTenantSettings;
using Flit.Admin.Application.Companies.Settings.UpdateTenantSettings;
using Flit.Admin.Application.Companies.Whitelist;
using Flit.Admin.Application.Companies.Whitelist.AddWhitelistEmails;
using Flit.Admin.Application.Companies.Whitelist.GetWhitelist;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints del Administrador de Compañías (HU #10189, #10190, #10191).
/// Todo el grupo exige rol SuperAdmin (RF01 / AC5).
/// </summary>
public static class AdminCompaniesEndpoints
{
    public static IEndpointRouteBuilder MapAdminCompaniesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy);

        // GET /api/v1/admin/companies/index — listado paginado con filtros (#10189 AC1, AC2).
        group.MapGet("/index", ListCompaniesAsync)
            .WithName("AdminCompaniesIndex");

        // GET /api/v1/admin/companies/{tenantId}/settings — configuración actual (#10190 AC3).
        group.MapGet("/{tenantId:guid}/settings", GetSettingsAsync)
            .WithName("AdminCompanyGetSettings");

        // PUT /api/v1/admin/companies/{tenantId}/settings — guardado atómico + audit (#10190 AC1/AC2).
        group.MapPut("/{tenantId:guid}/settings", UpdateSettingsAsync)
            .WithName("AdminCompanyUpdateSettings");

        // POST /api/v1/admin/companies/{tenantId}/whitelist — alta masiva + audit (#10191 AC4/AC5).
        group.MapPost("/{tenantId:guid}/whitelist", AddWhitelistAsync)
            .WithName("AdminCompanyAddWhitelist");

        // GET /api/v1/admin/companies/{tenantId}/whitelist — lista de correos exentos (#10191 AC6).
        group.MapGet("/{tenantId:guid}/whitelist", GetWhitelistAsync)
            .WithName("AdminCompanyGetWhitelist");

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

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Cuerpo de error de validación 422: <c>{ errors: [{ field, message }] }</c>.</summary>
    private sealed record ValidationErrorResponse(IReadOnlyList<SettingsValidationError> Errors);

    /// <summary>Cuerpo 201 del alta de whitelist: correos insertados y omitidos (duplicados).</summary>
    private sealed record WhitelistAddResponse(IReadOnlyList<string> Added, IReadOnlyList<string> Skipped);

    /// <summary>Cuerpo de error 422 de whitelist: <c>{ errors: [{ field, message, value }] }</c>.</summary>
    private sealed record WhitelistValidationErrorResponse(IReadOnlyList<WhitelistValidationError> Errors);
}
