using System.Security.Claims;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Application.Companies.Branding.GetBranding;
using Flit.Admin.Application.Companies.Branding.PublishBranding;
using Flit.Admin.Application.Companies.Branding.UploadBrandLogo;
using Flit.Admin.Application.Companies.Branding.UpsertBrandingDraft;
using Flit.Api.Authorization;
using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Catalog;
using Flit.Infrastructure.Notifications.Theme;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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

        // HU #12428 (AC1/AC2/AC6), HU #12431 AC1, HU #12414 AC4 — muestra de correo con el tema
        // propio (publicado o borrador) de la cabeza. Contrato: contratos-api.md §4.
        group.MapGet("/email-sample", GetEmailSampleAsync)
            .WithName("CompanyBrandingEmailSample")
            .WithSummary("Previsualiza una plantilla de correo con el tema propio de la cabeza")
            .Produces<NotificationTemplateSampleResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

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
    /// HU #12428 AC1/AC2/AC6, HU #12431 AC1, HU #12414 AC4 — muestra de correo con el tema PROPIO de
    /// la cabeza (nunca el de otra red, AC5 del contrato de sesión/pública se replica aquí por
    /// consistencia: el tenant sale del JWT, jamás de un parámetro). <c>source=published</c> sin
    /// marca publicada (borrador únicamente, o nada) responde <c>200</c> con
    /// <c>theme.kind = "flit"</c> — no <c>404</c>: la ausencia de marca publicada no es un error de
    /// la muestra, es el estado "aún no configurado" que el propio configurador ya distingue con
    /// <c>GET /company/branding</c> (AC4 del fallback: nunca falla por causa del tema).
    /// <c>source=draft</c> con un borrador incompleto completa los campos faltantes con FLIT y
    /// marca <c>theme.kind = "draft-partial"</c> (nunca <c>"brand"</c> a medias).
    /// </summary>
    private static async Task<IResult> GetEmailSampleAsync(
        [FromQuery] string? templateId,
        [FromQuery] string? source,
        HttpContext httpContext,
        [FromServices] GetBrandingHandler brandingHandler,
        [FromServices] IOptions<Flit.Admin.Application.Companies.Branding.BrandingOptions> brandingOptions,
        [FromServices] EmailThemePublicBrandingOptions publicBrandingOptions,
        CancellationToken cancellationToken)
    {
        var tenant = ResolveOwnTenant(httpContext.User);
        if (tenant is null)
        {
            return Results.Unauthorized();
        }

        var resolvedTemplateId = string.IsNullOrWhiteSpace(templateId) ? "tramites.aprobado" : templateId.Trim();
        var allowed = brandingOptions.Value.SampleTemplates;
        if (!allowed.Contains(resolvedTemplateId, StringComparer.Ordinal))
        {
            return Results.Json(
                new { error = BrandingErrors.SampleTemplateNotAllowed, allowed },
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!NotificationTemplateCatalog.TryResolve(resolvedTemplateId, out var descriptor))
        {
            return Results.Json(
                new { error = BrandingErrors.SampleTemplateNotAllowed, allowed },
                statusCode: StatusCodes.Status400BadRequest);
        }

        var useDraft = string.Equals(source, "draft", StringComparison.OrdinalIgnoreCase);

        var branding = await brandingHandler.HandleAsync(tenant.Value, cancellationToken).ConfigureAwait(false);

        EmailTheme theme;
        string kindOverride;
        if (useDraft)
        {
            var draft = branding?.Draft ?? BrandingDraft.Empty;
            theme = EmailThemeFactory.FromDraft(draft, publicBrandingOptions.PublicBaseUrl);
            kindOverride = draft.MissingFields().Count > 0 ? "draft-partial" : theme.KindWireValue;
        }
        else
        {
            // source=published (default) — sin marca publicada (o retirada), tema FLIT (AC4: nunca
            // falla ni responde 404 por esto).
            theme = branding is { IsRetired: false, Published: { } published }
                ? EmailThemeFactory.FromPublished(published, branding.PublishedVersion, publicBrandingOptions.PublicBaseUrl)
                : EmailTheme.Flit;
            kindOverride = theme.KindWireValue;
        }

        var (subject, html) = NotificationSampleRenderer.Render(
            descriptor.Id, NotificationChannel.FlitSmtp, theme: theme);

        var response = new NotificationTemplateSampleResponse(descriptor.Id, subject, html)
        {
            Theme = new EmailThemeInfoResponse(kindOverride, theme.PlatformName, theme.IsBrand ? theme.Version : null, SenderName: null),
        };

        return Results.Ok(response);
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
