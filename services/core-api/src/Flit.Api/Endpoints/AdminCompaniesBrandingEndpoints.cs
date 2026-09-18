using System.Security.Claims;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Application.Companies.Branding.GetBranding;
using Flit.Admin.Application.Companies.Branding.PublishBranding;
using Flit.Admin.Application.Companies.Branding.RetireBranding;
using Flit.Admin.Application.Companies.Branding.UploadBrandLogo;
using Flit.Admin.Application.Companies.Branding.UpsertBrandingDraft;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Consola del SuperAdmin sobre la identidad de marca de una cabeza MARCA_BLANCA (HU #12412,
/// Feature #12366, ADR-0060 D1). Contrato: <c>.claude/state/marca-blanca/diseno/contratos-api.md</c> §3.
/// Exclusivo SuperAdmin (a diferencia de <c>AdminCompaniesEndpoints</c>, sin <c>CompanyOwnTenantFilter</c>:
/// aquí no hay acceso de AdminCompany, ese es el grupo <see cref="CompanyBrandingEndpoints"/>).
/// </summary>
public static class AdminCompaniesBrandingEndpoints
{
    public static IEndpointRouteBuilder MapAdminCompaniesBrandingEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies/{tenantId:guid}/branding")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Identidad de marca");

        group.MapGet("", GetBrandingAsync)
            .WithName("AdminBrandingGet")
            .WithSummary("Obtiene la identidad de marca de una cabeza MARCA_BLANCA")
            .Produces<TenantBrandingResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("", UpsertBrandingAsync)
            .WithName("AdminBrandingUpsertDraft")
            .WithSummary("Crea o reemplaza el borrador de marca")
            .Produces<TenantBrandingResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/logo", UploadBrandLogoAsync)
            .WithName("AdminBrandingUploadLogo")
            .DisableAntiforgery()
            .WithSummary("Sube una versión nueva del logotipo de marca")
            .Produces<BrandLogoResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/publish", PublishBrandingAsync)
            .WithName("AdminBrandingPublish")
            .WithSummary("Publica el borrador vigente de la marca")
            .Produces<TenantBrandingResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/retire", RetireBrandingAsync)
            .WithName("AdminBrandingRetire")
            .WithSummary("Retira la marca publicada (se conserva el dato)")
            .Produces<TenantBrandingResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetBrandingAsync(
        Guid tenantId,
        [FromServices] GetBrandingHandler handler,
        CancellationToken cancellationToken)
    {
        var branding = await handler.HandleAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return branding is null
            ? Results.NotFound(new { error = BrandingErrors.NotFound, message = $"El tenant {tenantId} no tiene identidad de marca configurada." })
            : Results.Ok(TenantBrandingResponse.From(branding));
    }

    private static async Task<IResult> UpsertBrandingAsync(
        Guid tenantId,
        UpsertBrandingDraftRequest request,
        HttpContext httpContext,
        [FromServices] UpsertBrandingDraftHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new UpsertBrandingDraftCommand
        {
            TenantId = tenantId,
            Request = request,
            ChangedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return ToResult(result, tenantId);
    }

    private static async Task<IResult> PublishBrandingAsync(
        Guid tenantId,
        PublishBrandingBody? body,
        HttpContext httpContext,
        [FromServices] PublishBrandingHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new PublishBrandingCommand
        {
            TenantId = tenantId,
            RowVersion = body?.RowVersion,
            ChangedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            PublishBrandingOutcome.Published => Results.Ok(TenantBrandingResponse.From(result.Branding!)),
            PublishBrandingOutcome.NotFound => Results.NotFound(
                new { error = BrandingErrors.NotFound, message = $"El tenant {tenantId} no tiene identidad de marca configurada." }),
            PublishBrandingOutcome.Conflict => Results.Json(
                new { error = BrandingErrors.ConcurrencyConflict, message = "La marca fue modificada por otra persona. Recarga e intenta de nuevo." },
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(
                new { error = BrandingErrors.Incomplete, details = new { missing = result.Missing } },
                statusCode: StatusCodes.Status422UnprocessableEntity),
        };
    }

    private static async Task<IResult> RetireBrandingAsync(
        Guid tenantId,
        HttpContext httpContext,
        [FromServices] RetireBrandingHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new RetireBrandingCommand { TenantId = tenantId, ChangedBy = ResolveUserId(httpContext.User) };
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome == RetireBrandingOutcome.Retired
            ? Results.Ok(TenantBrandingResponse.From(result.Branding!))
            : Results.NotFound(new { error = BrandingErrors.NotFound, message = $"El tenant {tenantId} no tiene identidad de marca configurada." });
    }

    private static async Task<IResult> UploadBrandLogoAsync(
        Guid tenantId,
        IFormFile? file,
        HttpContext httpContext,
        [FromServices] UploadBrandLogoHandler handler,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: "Falta el archivo del logotipo.");
        }

        // IFormFile.OpenReadStream() ya es seekable (ASP.NET Core bufferiza el multipart); el sniffer
        // y el lector de dimensiones reposicionan al inicio tras leer la cabecera.
        await using var content = file.OpenReadStream();

        var command = new UploadBrandLogoCommand
        {
            TenantId = tenantId,
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
                new { error = BrandingErrors.TenantNotMarcaBlanca, message = $"El tenant {tenantId} no es una cabeza de tipo MARCA_BLANCA." },
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(new { errors = result.Errors }, statusCode: StatusCodes.Status422UnprocessableEntity),
        };
    }

    internal static IResult ToResult(UpsertBrandingDraftResult result, Guid tenantId) => result.Outcome switch
    {
        UpsertBrandingDraftOutcome.Saved => Results.Ok(TenantBrandingResponse.From(result.Branding!)),
        UpsertBrandingDraftOutcome.Conflict => Results.Json(
            new { error = BrandingErrors.ConcurrencyConflict, message = "La marca fue modificada por otra persona. Recarga e intenta de nuevo." },
            statusCode: StatusCodes.Status409Conflict),
        UpsertBrandingDraftOutcome.TenantNotMarcaBlanca => Results.Json(
            new { error = BrandingErrors.TenantNotMarcaBlanca, message = $"El tenant {tenantId} no es una cabeza de tipo MARCA_BLANCA." },
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Json(new { errors = result.Errors }, statusCode: StatusCodes.Status422UnprocessableEntity),
    };

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Cuerpo opcional de <c>POST .../branding/publish</c> (rowVersion para concurrencia optimista).</summary>
    public sealed record PublishBrandingBody(long? RowVersion);
}
