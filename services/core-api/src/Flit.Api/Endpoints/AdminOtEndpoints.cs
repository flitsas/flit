using System.Security.Claims;
using Flit.Admin.Application.OtClientProcedures;
using Flit.Admin.Application.OtClientProcedures.ApproveOtClientProcedure;
using Flit.Admin.Application.OtClientProcedures.GetOtClientProcedure;
using Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;
using Flit.Admin.Application.OtClientProcedures.RejectOtClientProcedure;
using Flit.Admin.Application.OtProfile.GetOtProfile;
using Flit.Admin.Application.OtProfile.UpdateOtFeatureFlag;
using Flit.Admin.Application.OtProfile.UpdateOtProfile;
using Flit.Admin.Application.OtWebhooks;
using Flit.Admin.Application.OtWebhooks.CreateOtWebhook;
using Flit.Admin.Application.OtWebhooks.ListOtApiLogs;
using Flit.Admin.Application.OtWebhooks.ListOtWebhooks;
using Flit.Admin.Application.OtWebhooks.UpdateOtWebhook;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de administración OT — perfil, modo Dashboard/QX, feature flags (HU #10215),
/// webhooks / bitácora API (HU #10216) y trámites de clientes tenant admin (HU #10217).
/// El tenant se resuelve exclusivamente del claim JWT <c>tenant_id</c> (AC5).
/// </summary>
public static class AdminOtEndpoints
{
    public static IEndpointRouteBuilder MapAdminOtEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/ot")
            .RequireAuthorization(AdminAuthorization.OtAdminPolicy)
            .WithTags("Admin · OT");

        group.MapGet("/profile", GetProfileAsync)
            .WithName("AdminOtGetProfile")
            .WithSummary("Obtiene el perfil OT del tenant autenticado")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPatch("/profile", UpdateProfileAsync)
            .WithName("AdminOtUpdateProfile")
            .WithSummary("Actualiza el perfil OT del tenant autenticado")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPatch("/feature-flags/{id:guid}", UpdateFeatureFlagAsync)
            .WithName("AdminOtUpdateFeatureFlag")
            .WithSummary("Activa o desactiva un feature flag OT")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/webhooks", CreateWebhookAsync)
            .WithName("AdminOtCreateWebhook")
            .WithSummary("Crea una suscripción webhook OT")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/webhooks", ListWebhooksAsync)
            .WithName("AdminOtListWebhooks")
            .WithSummary("Lista suscripciones webhook OT del tenant")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPatch("/webhooks/{id:guid}", UpdateWebhookAsync)
            .WithName("AdminOtUpdateWebhook")
            .WithSummary("Actualiza una suscripción webhook OT (hot-update)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/api-logs", ListApiLogsAsync)
            .WithName("AdminOtListApiLogs")
            .WithSummary("Consulta la bitácora de llamadas API OT")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/client-procedures", ListClientProceduresAsync)
            .WithName("AdminOtListClientProcedures")
            .WithSummary("Lista trámites de clientes con grant vigente hacia el OT")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/client-procedures/{id:guid}", GetClientProcedureAsync)
            .WithName("AdminOtGetClientProcedure")
            .WithSummary("Obtiene un trámite de cliente si el OT tiene grant vigente")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/client-procedures/{id:guid}/approve", ApproveClientProcedureAsync)
            .WithName("AdminOtApproveClientProcedure")
            .WithSummary("Aprueba un trámite pending_ot de un cliente OT")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/client-procedures/{id:guid}/reject", RejectClientProcedureAsync)
            .WithName("AdminOtRejectClientProcedure")
            .WithSummary("Rechaza un trámite pending_ot con motivo obligatorio")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> GetProfileAsync(
        HttpContext httpContext,
        GetOtProfileHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var response = await handler.HandleAsync(
            new GetOtProfileQuery { TenantId = tenantId },
            cancellationToken).ConfigureAwait(false);

        return Results.Ok(response);
    }

    private static async Task<IResult> UpdateProfileAsync(
        HttpContext httpContext,
        UpdateOtProfileRequest request,
        UpdateOtProfileHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(new UpdateOtProfileCommand
        {
            TenantId = tenantId,
            ChangedBy = ResolveUserId(httpContext.User),
            Request = request,
        }, cancellationToken).ConfigureAwait(false);

        if (!result.IsValid)
        {
            return Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        return Results.Ok(result.Profile);
    }

    private static async Task<IResult> UpdateFeatureFlagAsync(
        Guid id,
        HttpContext httpContext,
        UpdateOtFeatureFlagRequest request,
        UpdateOtFeatureFlagHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(new UpdateOtFeatureFlagCommand
        {
            TenantId = tenantId,
            FlagId = id,
            ChangedBy = ResolveUserId(httpContext.User),
            Request = request,
        }, cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            UpdateOtFeatureFlagStatus.NotFound => Results.NotFound(new { error = "Feature flag no encontrado" }),
            _ => Results.Ok(result.Flag),
        };
    }

    private static bool TryResolveTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        var claim = user.FindFirstValue(AdminAuthorization.TenantIdClaimType);
        return Guid.TryParse(claim, out tenantId);
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var userId) ? userId : null;
    }

    private static async Task<IResult> ListWebhooksAsync(
        HttpContext httpContext,
        ListOtWebhooksHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(
            new ListOtWebhooksQuery { TenantId = tenantId },
            cancellationToken).ConfigureAwait(false);

        return Results.Ok(new { data = result.Data });
    }

    private static async Task<IResult> CreateWebhookAsync(
        HttpContext httpContext,
        CreateOtWebhookRequest request,
        CreateOtWebhookHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(new CreateOtWebhookCommand
        {
            TenantId = tenantId,
            CreatedBy = ResolveUserId(httpContext.User),
            Request = request,
        }, cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            CreateOtWebhookStatus.ValidationFailed => Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Created($"/api/v1/admin/ot/webhooks/{result.Webhook!.Id}", result.Webhook),
        };
    }

    private static async Task<IResult> UpdateWebhookAsync(
        Guid id,
        HttpContext httpContext,
        UpdateOtWebhookRequest request,
        UpdateOtWebhookHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(new UpdateOtWebhookCommand
        {
            TenantId = tenantId,
            SubscriptionId = id,
            ChangedBy = ResolveUserId(httpContext.User),
            Request = request,
        }, cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            UpdateOtWebhookStatus.NotFound => Results.NotFound(new { error = "Webhook no encontrado" }),
            UpdateOtWebhookStatus.ValidationFailed => Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Ok(result.Webhook),
        };
    }

    private static async Task<IResult> ListApiLogsAsync(
        HttpContext httpContext,
        ListOtApiLogsHandler handler,
        string? direction,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(new ListOtApiLogsQuery
        {
            TenantId = tenantId,
            Direction = direction,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize,
        }, cancellationToken).ConfigureAwait(false);

        return Results.Ok(new
        {
            data = result.Data,
            totalCount = result.TotalCount,
            page = result.Page,
            pageSize = result.PageSize,
        });
    }

    private static async Task<IResult> ListClientProceduresAsync(
        HttpContext httpContext,
        ListOtClientProceduresHandler handler,
        string? status,
        Guid? procedureTypeId,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(new ListOtClientProceduresQuery
        {
            OtTenantId = tenantId,
            Status = status,
            ProcedureTypeId = procedureTypeId,
            Page = page,
            PageSize = pageSize,
        }, cancellationToken).ConfigureAwait(false);

        return Results.Ok(new
        {
            data = result.Data,
            totalCount = result.TotalCount,
            page = result.Page,
            pageSize = result.PageSize,
        });
    }

    private static async Task<IResult> GetClientProcedureAsync(
        Guid id,
        HttpContext httpContext,
        GetOtClientProcedureHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(new GetOtClientProcedureQuery
        {
            OtTenantId = tenantId,
            ProcedureInstanceId = id,
        }, cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            GetOtClientProcedureStatus.NotFound => Results.NotFound(new { error = "Trámite no encontrado" }),
            _ => Results.Ok(result.Procedure),
        };
    }

    private static async Task<IResult> ApproveClientProcedureAsync(
        Guid id,
        HttpContext httpContext,
        ApproveOtClientProcedureHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(new ApproveOtClientProcedureCommand
        {
            OtTenantId = tenantId,
            ProcedureInstanceId = id,
            ApprovedBy = ResolveUserId(httpContext.User),
        }, cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            ApproveOtClientProcedureStatus.NotFound => Results.NotFound(new { error = "Trámite no encontrado" }),
            ApproveOtClientProcedureStatus.InvalidState => Results.Conflict(new { error = "INVALID_STATE" }),
            _ => Results.Ok(result.Procedure),
        };
    }

    private static async Task<IResult> RejectClientProcedureAsync(
        Guid id,
        HttpContext httpContext,
        RejectOtClientProcedureRequest request,
        RejectOtClientProcedureHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(new RejectOtClientProcedureCommand
        {
            OtTenantId = tenantId,
            ProcedureInstanceId = id,
            RejectedBy = ResolveUserId(httpContext.User),
            Request = request,
        }, cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            RejectOtClientProcedureStatus.NotFound => Results.NotFound(new { error = "Trámite no encontrado" }),
            RejectOtClientProcedureStatus.InvalidState => Results.Conflict(new { error = "INVALID_STATE" }),
            RejectOtClientProcedureStatus.ValidationFailed => Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Ok(result.Procedure),
        };
    }
}
