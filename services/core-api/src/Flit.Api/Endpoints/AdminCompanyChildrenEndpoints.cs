using System.Security.Claims;
using Flit.Admin.Application.Companies.Children.CreateChildCompany;
using Flit.Admin.Application.Companies.Children.ListChildCompanies;
using Flit.Admin.Application.Companies.Children.SetChildCompanyStatus;
using Flit.Admin.Application.Companies.Children.UpdateChildCompany;
using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Application.Companies.SetCompanyStatus;
using Flit.Admin.Application.Companies.UpdateCompany;
using Flit.Admin.Domain.Companies;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #12345 — gestión directa de clientes hijos por la cabeza de grupo.
/// Parentesco validado explícitamente en cada handler (AC3); no usa
/// <see cref="CompanyOwnTenantFilter"/> sobre el segundo Guid.
/// </summary>
public static class AdminCompanyChildrenEndpoints
{
    public static IEndpointRouteBuilder MapAdminCompanyChildrenEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies/{headTenantId:guid}/children")
            .RequireAuthorization(AdminAuthorization.GroupHeadCompanyPolicy)
            .WithTags("Admin · Red de compañías");

        group.MapGet("", ListChildrenAsync)
            .WithName("AdminCompanyChildrenList")
            .WithSummary("Lista los clientes hijos de la cabeza de grupo")
            .Produces<IReadOnlyList<CompanyChildListItem>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/{childTenantId:guid}", GetChildAsync)
            .WithName("AdminCompanyChildrenGet")
            .WithSummary("Obtiene un cliente hijo propio")
            .Produces<CompanyChildListItem>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("", CreateChildAsync)
            .WithName("AdminCompanyChildrenCreate")
            .WithSummary("Crea un cliente hijo de la cabeza de grupo")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/{childTenantId:guid}", UpdateChildAsync)
            .WithName("AdminCompanyChildrenUpdate")
            .WithSummary("Edita un cliente hijo propio")
            .Produces<CompanyListItem>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/{childTenantId:guid}/status", SetChildStatusAsync)
            .WithName("AdminCompanyChildrenSetStatus")
            .WithSummary("Activa o desactiva un cliente hijo propio")
            .Produces<CompanyListItem>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListChildrenAsync(
        Guid headTenantId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] ListChildCompaniesHandler handler,
        CancellationToken cancellationToken)
    {
        var forbid = await GroupHeadTenantAccess
            .EnsureHeadCallerAsync(user, headTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var items = await handler.HandleAsync(headTenantId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(items);
    }

    private static async Task<IResult> GetChildAsync(
        Guid headTenantId,
        Guid childTenantId,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] ListChildCompaniesHandler handler,
        CancellationToken cancellationToken)
    {
        var forbid = await GroupHeadTenantAccess
            .EnsureHeadAndChildAsync(user, headTenantId, childTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var item = await handler.GetAsync(headTenantId, childTenantId, cancellationToken).ConfigureAwait(false);
        return item is null
            ? Results.NotFound(new { error = "NOT_FOUND" })
            : Results.Ok(item);
    }

    private static async Task<IResult> CreateChildAsync(
        Guid headTenantId,
        CreateCompanyRequest request,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] CreateChildCompanyHandler handler,
        CancellationToken cancellationToken)
    {
        var forbid = await GroupHeadTenantAccess
            .EnsureHeadCallerAsync(user, headTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var head = await hierarchy.GetHierarchyInfoAsync(headTenantId, cancellationToken).ConfigureAwait(false);
        if (head is null)
        {
            return Results.NotFound(new { error = "HEAD_NOT_FOUND", message = "No existe la cabeza de grupo." });
        }

        var result = await handler.HandleAsync(
            new CreateChildCompanyCommand
            {
                HeadTenantId = headTenantId,
                Request = request,
                CreatedBy = ResolveUserId(user),
                HeadTenantType = head.TenantType,
            },
            cancellationToken).ConfigureAwait(false);

        return result.IsValid
            ? Results.Created($"/api/v1/admin/companies/{headTenantId}/children/{result.Company!.Id}", result.Company)
            : Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> UpdateChildAsync(
        Guid headTenantId,
        Guid childTenantId,
        UpdateCompanyRequest request,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] UpdateChildCompanyHandler handler,
        CancellationToken cancellationToken)
    {
        var forbid = await GroupHeadTenantAccess
            .EnsureHeadAndChildAsync(user, headTenantId, childTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler.HandleAsync(
            new UpdateChildCompanyCommand
            {
                HeadTenantId = headTenantId,
                ChildTenantId = childTenantId,
                Request = request,
                ChangedBy = ResolveUserId(user),
            },
            cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            UpdateCompanyOutcome.Updated => Results.Ok(result.Company),
            UpdateCompanyOutcome.NotFound => Results.NotFound(new { error = "NOT_FOUND" }),
            _ => Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity),
        };
    }

    private static async Task<IResult> SetChildStatusAsync(
        Guid headTenantId,
        Guid childTenantId,
        SetCompanyStatusRequest request,
        ClaimsPrincipal user,
        [FromServices] ICompanyHierarchyRepository hierarchy,
        [FromServices] SetChildCompanyStatusHandler handler,
        CancellationToken cancellationToken)
    {
        var forbid = await GroupHeadTenantAccess
            .EnsureHeadAndChildAsync(user, headTenantId, childTenantId, hierarchy, cancellationToken)
            .ConfigureAwait(false);
        if (forbid is not null)
        {
            return forbid;
        }

        var result = await handler.HandleAsync(
            new SetChildCompanyStatusCommand
            {
                HeadTenantId = headTenantId,
                ChildTenantId = childTenantId,
                EstadoActivo = request.EstadoActivo,
                ChangedBy = ResolveUserId(user),
            },
            cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            SetCompanyStatusOutcome.Updated => Results.Ok(result.Company),
            _ => Results.NotFound(new { error = "NOT_FOUND" }),
        };
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
