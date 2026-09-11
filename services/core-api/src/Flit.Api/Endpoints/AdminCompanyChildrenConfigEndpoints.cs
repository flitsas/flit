using System.Security.Claims;
using Flit.Admin.Application.Companies.Settings.GetTenantSettings;
using Flit.Admin.Application.Companies.Settings.UpdateTenantSettings;
using Flit.Admin.Application.Companies.TransitOffices.GetTenantAuditLog;
using Flit.Admin.Application.Companies.Whitelist.AddWhitelistEmails;
using Flit.Admin.Application.Companies.Whitelist.GetWhitelist;
using Flit.Admin.Domain.Companies;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #12353 — configuración de clientes hijos ejercida por la cabeza de grupo.
/// Reutiliza handlers existentes pasando <c>childTenantId</c> como objetivo.
/// No incluye transit-grants (Feature #12256).
/// </summary>
public static class AdminCompanyChildrenConfigEndpoints
{
    public static IEndpointRouteBuilder MapAdminCompanyChildrenConfigEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies/{headTenantId:guid}/children/{childTenantId:guid}")
            .RequireAuthorization(AdminAuthorization.GroupHeadCompanyPolicy)
            .WithTags("Admin · Configuración de hijos");

        group.MapGet("/settings", GetSettingsAsync)
            .WithName("AdminCompanyChildGetSettings");

        group.MapPut("/settings", UpdateSettingsAsync)
            .WithName("AdminCompanyChildUpdateSettings");

        group.MapPost("/whitelist", AddWhitelistAsync)
            .WithName("AdminCompanyChildAddWhitelist");

        group.MapGet("/whitelist", GetWhitelistAsync)
            .WithName("AdminCompanyChildGetWhitelist");

        group.MapGet("/audit-log", GetAuditLogAsync)
            .WithName("AdminCompanyChildGetAuditLog");

        // Mandatarios, representantes, baúl y documentos personalizados — misma consola, tenant = hijo.
        group.MapGroup("/mandate-signers")
            .MapAdminCompanyMandateSignersChildRoutes();

        group.MapGroup("/legal-representatives")
            .MapAdminCompanyLegalRepresentativesChildRoutes();

        group.MapGroup("/signature-vault")
            .MapAdminCompanySignatureVaultChildRoutes();

        group.MapGroup("/personalized-documents")
            .MapAdminCompanyPersonalizedDocumentsChildRoutes();

        group.MapGroup("/deeds")
            .MapAdminCompanyDeedsChildRoutes();

        group.MapGroup("/document-params")
            .MapAdminCompanyDocumentParamsChildRoutes();

        return app;
    }

    private static async Task<IResult?> GuardChildWriteAsync(
        ClaimsPrincipal user,
        Guid headTenantId,
        Guid childTenantId,
        ICompanyHierarchyRepository hierarchy,
        CancellationToken ct)
    {
        return await GroupHeadTenantAccess
            .EnsureHeadAndChildAsync(user, headTenantId, childTenantId, hierarchy, ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> GetSettingsAsync(
        Guid headTenantId,
        Guid childTenantId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] GetTenantSettingsHandler handler,
        CancellationToken cancellationToken)
    {
        var forbid = await GuardChildWriteAsync(user, headTenantId, childTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler
            .HandleAsync(new GetTenantSettingsQuery { TenantId = childTenantId }, cancellationToken)
            .ConfigureAwait(false);

        return result is null
            ? Results.NotFound(new { error = "NOT_FOUND" })
            : Results.Ok(result);
    }

    private static async Task<IResult> UpdateSettingsAsync(
        Guid headTenantId,
        Guid childTenantId,
        UpdateTenantSettingsRequest request,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] UpdateTenantSettingsHandler handler,
        CancellationToken cancellationToken)
    {
        var forbid = await GuardChildWriteAsync(user, headTenantId, childTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler.HandleAsync(
            new UpdateTenantSettingsCommand
            {
                TenantId = childTenantId,
                Request = request,
                ChangedBy = ResolveUserId(user),
            },
            cancellationToken).ConfigureAwait(false);

        return result.IsValid
            ? Results.Ok(result.Settings)
            : Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> AddWhitelistAsync(
        Guid headTenantId,
        Guid childTenantId,
        AddWhitelistEmailsRequest request,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] AddWhitelistEmailsHandler handler,
        CancellationToken cancellationToken)
    {
        var forbid = await GuardChildWriteAsync(user, headTenantId, childTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler.HandleAsync(
            new AddWhitelistEmailsCommand
            {
                TenantId = childTenantId,
                Request = request,
                AddedBy = ResolveUserId(user),
            },
            cancellationToken).ConfigureAwait(false);

        return result.IsValid
            ? Results.Created(
                $"/api/v1/admin/companies/{headTenantId}/children/{childTenantId}/whitelist",
                new { added = result.AddedEmails, skipped = result.SkippedEmails })
            : Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message, value = e.Value }) },
                statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> GetWhitelistAsync(
        Guid headTenantId,
        Guid childTenantId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] GetWhitelistHandler handler,
        CancellationToken cancellationToken)
    {
        var forbid = await GuardChildWriteAsync(user, headTenantId, childTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler
            .HandleAsync(new GetWhitelistQuery { TenantId = childTenantId }, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetAuditLogAsync(
        Guid headTenantId,
        Guid childTenantId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] GetTenantAuditLogHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var forbid = await GuardChildWriteAsync(user, headTenantId, childTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler.HandleAsync(
            new GetTenantAuditLogQuery { TenantId = childTenantId, Page = page, PageSize = pageSize },
            cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    internal static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
