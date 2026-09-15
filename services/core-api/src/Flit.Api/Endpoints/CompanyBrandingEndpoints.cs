using System.Security.Claims;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Application.Companies.Branding.GetBranding;
using Flit.Admin.Application.Companies.Branding.PublishBranding;
using Flit.Admin.Application.Companies.Branding.UploadBrandLogo;
using Flit.Admin.Application.Companies.Branding.UpsertBrandingDraft;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Autogestión de la marca por la propia cabeza de grupo (HU #12412 AC3/AC4, Feature #12366).
/// Contrato: <c>.claude/state/marca-blanca/diseno/contratos-api.md</c> §4. Sin <c>{tenantId}</c> en la
/// ruta: el tenant sale del JWT (<see cref="RequestTenantResolver"/>), nunca de un header/param
/// crudo. La policy <see cref="AdminAuthorization.GroupHeadCompanyPolicy"/> ya exige
/// <c>is_group_parent = true</c> sobre el tenant del caller (HU #12345 AC4) — un hijo o una compañía
/// sin red no la superan (403 uniforme, sin revelar nada, AC3). No existe
/// <c>POST /company/branding/retire</c>: retirar es exclusivo del SuperAdmin.
/// </summary>
public static class CompanyBrandingEndpoints
{
    public static IEndpointRouteBuilder MapCompanyBrandingEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/company/branding")
            .RequireAuthorization(AdminAuthorization.GroupHeadCompanyPolicy)
            .WithTags("Compañía · Identidad de marca");

        group.MapGet("", GetBrandingAsync)
            .WithName("CompanyBrandingGet")
            .WithSummary("Obtiene la identidad de marca de la propia cabeza de grupo")
            .Produces<TenantBrandingResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("", UpsertBrandingAsync)
            .WithName("CompanyBrandingUpsertDraft")
            .WithSummary("Crea o reemplaza el borrador de marca de la propia cabeza")
            .Produces<TenantBrandingResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/logo", UploadBrandLogoAsync)
            .WithName("CompanyBrandingUploadLogo")
            .DisableAntiforgery()
            .WithSummary("Sube una versión nueva del logotipo de la propia cabeza")
            .Produces<BrandLogoResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/publish", PublishBrandingAsync)
            .WithName("CompanyBrandingPublish")
            .WithSummary("Publica el borrador vigente de la propia cabeza")
            .Produces<TenantBrandingResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> GetBrandingAsync(
        HttpContext httpContext,
        [FromServices] GetBrandingHandler handler,
        CancellationToken cancellationToken)
    {
        var tenant = ResolveOwnTenant(httpContext.User);
        if (tenant is null)
        {
            return Results.Unauthorized();
        }

        var branding = await handler.HandleAsync(tenant.Value, cancellationToken).ConfigureAwait(false);
        return branding is null
            ? Results.NotFound(new { error = BrandingErrors.NotFound, message = "Tu compañía aún no tiene una configuración inicial de marca." })
            : Results.Ok(TenantBrandingResponse.From(branding));
    }

    private static async Task<IResult> UpsertBrandingAsync(
        UpsertBrandingDraftRequest request,
        HttpContext httpContext,
        [FromServices] UpsertBrandingDraftHandler handler,
        CancellationToken cancellationToken)
    {
        var tenant = ResolveOwnTenant(httpContext.User);
        if (tenant is null)
        {
            return Results.Unauthorized();
        }

        var command = new UpsertBrandingDraftCommand
        {
            TenantId = tenant.Value,
            Request = request,
            ChangedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return AdminCompaniesBrandingEndpoints.ToResult(result, tenant.Value);
    }

    private static async Task<IResult> PublishBrandingAsync(
        AdminCompaniesBrandingEndpoints.PublishBrandingBody? body,
        HttpContext httpContext,
        [FromServices] PublishBrandingHandler handler,
        CancellationToken cancellationToken)
    {
        var tenant = ResolveOwnTenant(httpContext.User);
        if (tenant is null)
        {
            return Results.Unauthorized();
        }

        var command = new PublishBrandingCommand
        {
            TenantId = tenant.Value,
            RowVersion = body?.RowVersion,
            ChangedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            PublishBrandingOutcome.Published => Results.Ok(TenantBrandingResponse.From(result.Branding!)),
            PublishBrandingOutcome.NotFound => Results.NotFound(
                new { error = BrandingErrors.NotFound, message = "Tu compañía aún no tiene una configuración inicial de marca." }),
            PublishBrandingOutcome.Conflict => Results.Json(
                new { error = BrandingErrors.ConcurrencyConflict, message = "La marca fue modificada por otra persona. Recarga e intenta de nuevo." },
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(
                new { error = BrandingErrors.Incomplete, details = new { missing = result.Missing } },
                statusCode: StatusCodes.Status422UnprocessableEntity),
        };
    }

    private static async Task<IResult> UploadBrandLogoAsync(
        IFormFile? file,
        HttpContext httpContext,
        [FromServices] UploadBrandLogoHandler handler,
        CancellationToken cancellationToken)
    {
        var tenant = ResolveOwnTenant(httpContext.User);
        if (tenant is null)
        {
            return Results.Unauthorized();
        }

        if (file is null || file.Length == 0)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: "Falta el archivo del logotipo.");
        }

        await using var content = file.OpenReadStream();

        var command = new UploadBrandLogoCommand
        {
            TenantId = tenant.Value,
            Filename = file.FileName,
            Content = content,
            ChangedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            UploadBrandLogoOutcome.Uploaded => Results.Created(
                $"/api/v1/public/branding/logos/{result.Logo!.Id}", BrandLogoResponse.From(result.Logo)),
            UploadBrandLogoOutcome.TenantNotMarcaBlanca => Results.Json(
                new { error = BrandingErrors.TenantNotMarcaBlanca, message = "Tu compañía no es una cabeza de tipo MARCA_BLANCA." },
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(new { errors = result.Errors }, statusCode: StatusCodes.Status422UnprocessableEntity),
        };
    }

    /// <summary>
    /// Tenant propio del caller (HU #12412 AC3): SuperAdmin no tiene "tenant propio" para este grupo
    /// autogestionado (opera por <c>/admin/companies/{tenantId}/branding</c>), así que si no resuelve
    /// un <c>tenant_id</c> no vacío del JWT se rechaza — nunca se interpreta como "todos".
    /// </summary>
    private static Guid? ResolveOwnTenant(ClaimsPrincipal user) =>
        RequestTenantResolver.TryResolveNonEmptyTenantId(user, out var tenantId) ? tenantId : null;

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
