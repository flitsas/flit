using System.Security.Claims;
using Flit.Admin.Application.OtClientProcedures;
using Flit.Admin.Application.OtClientProcedures.ApproveOtClientProcedure;
using Flit.Admin.Application.OtClientProcedures.GetOtClientProcedure;
using Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;
using Flit.Admin.Application.OtClientProcedures.RejectOtClientProcedure;
using Flit.Admin.Application.OtDocumentPrecedence;
using Flit.Admin.Application.OtDocumentPrecedence.ListOtDocumentPrecedence;
using Flit.Admin.Application.OtDocumentPrecedence.UpdateOtDocumentPrecedence;
using Flit.Admin.Application.OtDocumentTags;
using Flit.Admin.Application.OtDocumentTags.CreateOtDocumentTag;
using Flit.Admin.Application.OtDocumentTags.DeleteOtDocumentTag;
using Flit.Admin.Application.OtDocumentTags.ListOtDocumentTags;
using Flit.Admin.Application.OtRules;
using Flit.Admin.Application.OtRules.CreateOtRule;
using Flit.Admin.Application.OtRules.ListOtRules;
using Flit.Admin.Application.OtRules.UpdateOtRule;
using Flit.Admin.Application.OtProfile.GetOtProfile;
using Flit.Admin.Application.OtProfile.UpdateOtFeatureFlag;
using Flit.Admin.Application.OtProfile.UpdateOtProfile;
using Flit.Admin.Application.OtWebhooks;
using Flit.Admin.Application.OtWebhooks.CreateOtWebhook;
using Flit.Admin.Application.OtWebhooks.ListOtApiLogs;
using Flit.Admin.Application.OtWebhooks.ListOtWebhooks;
using Flit.Admin.Application.OtWebhooks.UpdateOtWebhook;
using Flit.Api.Authorization;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de administración OT — perfil, modo Dashboard/QX, feature flags (HU #10215),
/// webhooks / bitácora API (HU #10216), trámites de clientes tenant admin (HU #10217),
/// motor de reglas (HU #10221) y prelación/etiquetas documentales (HU #10222).
/// El tenant se resuelve exclusivamente del claim JWT <c>tenant_id</c> (AC5).
/// </summary>
public static class AdminOtEndpoints
{
    public static IEndpointRouteBuilder MapAdminOtEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/ot")
            .RequireAuthorization(AdminAuthorization.OtModulePolicy)
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

        group.MapPost("/rules", CreateRuleAsync)
            .WithName("AdminOtCreateRule")
            .WithSummary("Crea una regla OT con condiciones AND/OR")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/rules", ListRulesAsync)
            .WithName("AdminOtListRules")
            .WithSummary("Lista reglas OT del tenant")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPatch("/rules/{id:guid}", UpdateRuleAsync)
            .WithName("AdminOtUpdateRule")
            .WithSummary("Activa o desactiva una regla OT (hot-swap)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/document-precedence", ListDocumentPrecedenceAsync)
            .WithName("AdminOtListDocumentPrecedence")
            .WithSummary("Lista prelación documental por tipo de trámite")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPatch("/document-precedence", UpdateDocumentPrecedenceAsync)
            .WithName("AdminOtUpdateDocumentPrecedence")
            .WithSummary("Reordena prelación documental en batch atómico")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/document-tags", CreateDocumentTagAsync)
            .WithName("AdminOtCreateDocumentTag")
            .WithSummary("Crea una etiqueta documental OT")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/document-tags", ListDocumentTagsAsync)
            .WithName("AdminOtListDocumentTags")
            .WithSummary("Lista etiquetas documentales del tenant")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapDelete("/document-tags/{id:guid}", DeleteDocumentTagAsync)
            .WithName("AdminOtDeleteDocumentTag")
            .WithSummary("Elimina una etiqueta documental OT")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // ── Self-service de usuarios OT (refactor adminOT) ─────────────────────────
        // Mismo grupo/policy (OtModulePolicy: SuperAdmin + ot_admin). Sirve tanto para
        // que SuperAdmin invite al primer adminOT de un tenant recién creado
        // (?transitOfficeId=, resuelto contra admin.transit_office_profiles) como para
        // que un ot_admin autogestione sus propios colaboradores (siempre su tenant del
        // JWT, ignora el query param). No se crean roles personalizados: todo invitado
        // recibe el único rol ot_admin del tenant.
        group.MapPost("/users/invite", InviteUserAsync)
            .WithName("AdminOtInviteUser")
            .WithSummary("Invita un usuario al tenant OT")
            .WithDescription("Crea una invitación por email con el rol ot_admin del tenant resuelto (propio "
                + "para ot_admin, o el indicado por ?transitOfficeId= para SuperAdmin). 409 si ya hay una "
                + "invitación pendiente o si el correo ya tiene cuenta.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/users", ListUsersAsync)
            .WithName("AdminOtListUsers")
            .WithSummary("Lista los usuarios del tenant OT")
            .WithDescription("Usuarios activos, sin rol e invitaciones pendientes del tenant resuelto (propio "
                + "para ot_admin, o el indicado por ?transitOfficeId= para SuperAdmin).")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/users/{userId:guid}/suspend", SuspendUserAsync)
            .WithName("AdminOtSuspendUser")
            .WithSummary("Suspende temporalmente a un usuario del tenant OT")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/users/{userId:guid}/suspend", UnsuspendUserAsync)
            .WithName("AdminOtUnsuspendUser")
            .WithSummary("Levanta la suspensión activa de un usuario del tenant OT")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetProfileAsync(
        HttpContext httpContext,
        GetOtProfileHandler handler,
        ITransitOfficeCatalog transitOfficeCatalog,
        [FromQuery] Guid? transitOfficeId,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var scopedOfficeId = default(Guid?);
        if (!TryResolveScopedTransitOfficeId(
                httpContext.User,
                transitOfficeId,
                transitOfficeCatalog,
                out scopedOfficeId,
                out var officeError))
        {
            return officeError!;
        }

        var response = await handler.HandleAsync(
            new GetOtProfileQuery
            {
                TenantId = tenantId,
                TransitOfficeId = scopedOfficeId,
            },
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

    private static bool IsSuperAdmin(ClaimsPrincipal user) =>
        user.IsInRole(AdminAuthorization.SuperAdminRole);

    /// <summary>
    /// SuperAdmin puede fijar la OT del hub vía query; ot_admin ignora el parámetro
    /// y usa su perfil persistido.
    /// </summary>
    private static bool TryResolveScopedTransitOfficeId(
        ClaimsPrincipal user,
        Guid? transitOfficeId,
        ITransitOfficeCatalog transitOfficeCatalog,
        out Guid? scopedOfficeId,
        out IResult? errorResult)
    {
        scopedOfficeId = null;
        errorResult = null;

        if (!IsSuperAdmin(user) || transitOfficeId is null || transitOfficeId == Guid.Empty)
        {
            return true;
        }

        if (!transitOfficeCatalog.Exists(transitOfficeId.Value))
        {
            errorResult = Results.BadRequest(new { error = "transitOfficeId no existe en el catálogo OT" });
            return false;
        }

        scopedOfficeId = transitOfficeId.Value;
        return true;
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
        short? minResponseCode,
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
            MinResponseCode = minResponseCode,
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
        ITransitOfficeCatalog transitOfficeCatalog,
        string? status,
        Guid? procedureTypeId,
        int? page,
        int? pageSize,
        [FromQuery] Guid? transitOfficeId,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var scopedOfficeId = default(Guid?);
        if (!TryResolveScopedTransitOfficeId(
                httpContext.User,
                transitOfficeId,
                transitOfficeCatalog,
                out scopedOfficeId,
                out var officeError))
        {
            return officeError!;
        }

        var result = await handler.HandleAsync(new ListOtClientProceduresQuery
        {
            OtTenantId = tenantId,
            TransitOfficeId = scopedOfficeId,
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
            ApproveOtClientProcedureStatus.QuipuxReadOnly => Results.Json(
                new { error = "QUIPUX_READONLY" },
                statusCode: StatusCodes.Status403Forbidden),
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
            RejectOtClientProcedureStatus.QuipuxReadOnly => Results.Json(
                new { error = "QUIPUX_READONLY" },
                statusCode: StatusCodes.Status403Forbidden),
            RejectOtClientProcedureStatus.ValidationFailed => Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Ok(result.Procedure),
        };
    }

    private static async Task<IResult> CreateRuleAsync(
        HttpContext httpContext,
        CreateOtRuleRequest request,
        CreateOtRuleHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(new CreateOtRuleCommand
        {
            TenantId = tenantId,
            CreatedBy = ResolveUserId(httpContext.User),
            Request = request,
        }, cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            CreateOtRuleStatus.ValidationFailed => Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Created($"/api/v1/admin/ot/rules/{result.Rule!.Id}", result.Rule),
        };
    }

    private static async Task<IResult> ListRulesAsync(
        HttpContext httpContext,
        ListOtRulesHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(
            new ListOtRulesQuery { TenantId = tenantId },
            cancellationToken).ConfigureAwait(false);

        return Results.Ok(new { data = result.Data });
    }

    private static async Task<IResult> UpdateRuleAsync(
        Guid id,
        HttpContext httpContext,
        UpdateOtRuleRequest request,
        UpdateOtRuleHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(new UpdateOtRuleCommand
        {
            TenantId = tenantId,
            RuleId = id,
            ChangedBy = ResolveUserId(httpContext.User),
            Request = request,
        }, cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            UpdateOtRuleStatus.NotFound => Results.NotFound(new { error = "Regla no encontrada" }),
            UpdateOtRuleStatus.ValidationFailed => Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Ok(result.Rule),
        };
    }

    private static async Task<IResult> ListDocumentPrecedenceAsync(
        HttpContext httpContext,
        ListOtDocumentPrecedenceHandler handler,
        Guid? procedureTypeId,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (procedureTypeId is null || procedureTypeId == Guid.Empty)
        {
            return Results.Json(
                new { errors = new[] { new { field = "procedureTypeId", message = "PROCEDURE_TYPE_REQUIRED" } } },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var result = await handler.HandleAsync(new ListOtDocumentPrecedenceQuery
        {
            TenantId = tenantId,
            ProcedureTypeId = procedureTypeId.Value,
        }, cancellationToken).ConfigureAwait(false);

        return Results.Ok(new { data = result.Data });
    }

    private static async Task<IResult> UpdateDocumentPrecedenceAsync(
        HttpContext httpContext,
        UpdateOtDocumentPrecedenceRequest request,
        UpdateOtDocumentPrecedenceHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(new UpdateOtDocumentPrecedenceCommand
        {
            TenantId = tenantId,
            ChangedBy = ResolveUserId(httpContext.User),
            Request = request,
        }, cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            UpdateOtDocumentPrecedenceStatus.ValidationFailed => Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Ok(new { data = result.Data }),
        };
    }

    private static async Task<IResult> CreateDocumentTagAsync(
        HttpContext httpContext,
        CreateOtDocumentTagRequest request,
        CreateOtDocumentTagHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(new CreateOtDocumentTagCommand
        {
            TenantId = tenantId,
            CreatedBy = ResolveUserId(httpContext.User),
            Request = request,
        }, cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            CreateOtDocumentTagStatus.DuplicateCode => Results.Conflict(new { error = "TAG_CODE_DUPLICATE" }),
            CreateOtDocumentTagStatus.ValidationFailed => Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Created($"/api/v1/admin/ot/document-tags/{result.Tag!.Id}", result.Tag),
        };
    }

    private static async Task<IResult> ListDocumentTagsAsync(
        HttpContext httpContext,
        ListOtDocumentTagsHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(
            new ListOtDocumentTagsQuery { TenantId = tenantId },
            cancellationToken).ConfigureAwait(false);

        return Results.Ok(new { data = result.Data });
    }

    private static async Task<IResult> DeleteDocumentTagAsync(
        Guid id,
        HttpContext httpContext,
        DeleteOtDocumentTagHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(new DeleteOtDocumentTagCommand
        {
            TenantId = tenantId,
            TagId = id,
        }, cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            DeleteOtDocumentTagStatus.NotFound => Results.NotFound(new { error = "Etiqueta no encontrada" }),
            _ => Results.NoContent(),
        };
    }

    // ── Self-service de usuarios OT (refactor adminOT) ─────────────────────────────

    private static async Task<IResult> InviteUserAsync(
        HttpContext httpContext,
        InviteOtUserRequest request,
        FlitDbContext db,
        CreateInvitationHandler handler,
        [FromQuery] Guid? transitOfficeId,
        CancellationToken cancellationToken)
    {
        var (tenantId, scopeError) = await ResolveOtUserScopeAsync(
            httpContext.User, transitOfficeId, db, cancellationToken).ConfigureAwait(false);
        if (scopeError is not null)
        {
            return scopeError;
        }

        var invitedBy = ResolveUserId(httpContext.User);
        if (invitedBy is null)
        {
            return Results.Unauthorized();
        }

        // El rol destino se resuelve automáticamente: en un tenant OT solo existe el
        // rol de sistema ot_admin (sin roles personalizados — decisión de alcance v1).
        var role = await db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId && r.Code == TransitOfficeTenantWriteRepositoryRoleCode,
                cancellationToken)
            .ConfigureAwait(false);

        if (role is null)
        {
            return Results.Json(
                new { error = "ROLE_NOT_FOUND", message = "El tenant OT no tiene configurado el rol ot_admin." },
                statusCode: StatusCodes.Status409Conflict);
        }

        try
        {
            var result = await handler.HandleAsync(
                new CreateInvitationCommand(
                    tenantId, request.Email, request.FullName ?? string.Empty, role.Id, invitedBy.Value),
                cancellationToken).ConfigureAwait(false);

            return Results.Created(
                $"/api/v1/admin/ot/users/invite/{result.InvitationId}",
                new { invitationId = result.InvitationId, email = result.Email, emailSent = result.EmailSent });
        }
        catch (RoleNotFoundException)
        {
            return Results.Json(
                new { error = "ROLE_NOT_FOUND", message = "El rol ot_admin no existe en el tenant." },
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvitationAlreadyPendingException)
        {
            return Results.Json(
                new { error = "INVITATION_ALREADY_PENDING", message = "Ya existe una invitación pendiente para este correo." },
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (UserAlreadyExistsException)
        {
            return Results.Json(
                new { error = "USER_ALREADY_EXISTS", message = "Este correo ya tiene una cuenta activa en el sistema." },
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> ListUsersAsync(
        HttpContext httpContext,
        FlitDbContext db,
        [FromQuery] Guid? transitOfficeId,
        CancellationToken cancellationToken)
    {
        var (tenantId, scopeError) = await ResolveOtUserScopeAsync(
            httpContext.User, transitOfficeId, db, cancellationToken).ConfigureAwait(false);
        if (scopeError is not null)
        {
            return scopeError;
        }

        var now = DateTimeOffset.UtcNow;

        var activeUsers = await (
            from a in db.UserRoleAssignments.AsNoTracking()
            join u in db.Users.AsNoTracking() on a.UserId equals u.Id
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
            where a.TenantId == tenantId && a.DeletedAt == null && u.DeletedAt == null
            select new OtUserDto(
                u.Id.ToString(),
                u.DisplayName,
                u.Email,
                r.Name,
                r.Code,
                a.RoleId,
                u.Status == "active" ? "active" : "inactive",
                null,
                db.UserTempSuspensions.Any(s => s.UserId == u.Id && s.TenantId == tenantId
                    && s.DeletedAt == null && s.StartsAt <= now && s.EndsAt >= now))
        ).ToListAsync(cancellationToken).ConfigureAwait(false);

        var usersWithoutRole = await (
            from u in db.Users.AsNoTracking()
            where u.HomeTenantId == tenantId
                  && u.DeletedAt == null
                  && !db.UserRoleAssignments.Any(a => a.UserId == u.Id && a.TenantId == tenantId && a.DeletedAt == null)
            select new OtUserDto(
                u.Id.ToString(),
                u.DisplayName,
                u.Email,
                null,
                null,
                null,
                u.Status == "active" ? "active" : "inactive",
                null,
                db.UserTempSuspensions.Any(s => s.UserId == u.Id && s.TenantId == tenantId
                    && s.DeletedAt == null && s.StartsAt <= now && s.EndsAt >= now))
        ).ToListAsync(cancellationToken).ConfigureAwait(false);

        var pending = await db.UserInvitations
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Status == "pending")
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new OtUserDto(
                x.Id.ToString(), x.FullName, x.Email, null, null, null, "pending", x.CreatedAt, false))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new { data = activeUsers.Concat(usersWithoutRole).Concat(pending).ToList() });
    }

    private static async Task<IResult> SuspendUserAsync(
        Guid userId,
        HttpContext httpContext,
        SuspendOtUserRequest request,
        FlitDbContext db,
        [FromQuery] Guid? transitOfficeId,
        CancellationToken cancellationToken)
    {
        var (tenantId, scopeError) = await ResolveOtUserScopeAsync(
            httpContext.User, transitOfficeId, db, cancellationToken).ConfigureAwait(false);
        if (scopeError is not null)
        {
            return scopeError;
        }

        var callerId = ResolveUserId(httpContext.User);
        if (callerId == userId)
        {
            return Results.BadRequest(new { error = "SELF_SUSPEND", message = "No puedes suspenderte a ti mismo." });
        }

        // El scope ya garantiza que ot_admin solo puede resolver su propio tenant (no
        // recibe transitOfficeId de otro OT); este chequeo evita además que se
        // suspenda a un usuario que no pertenece al tenant resuelto.
        var userExistsInTenant = await db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.DeletedAt == null
                && (db.UserRoleAssignments.Any(a => a.UserId == userId && a.TenantId == tenantId && a.DeletedAt == null)
                    || u.HomeTenantId == tenantId),
                cancellationToken).ConfigureAwait(false);

        if (!userExistsInTenant)
        {
            return Results.NotFound(new { error = "USER_NOT_FOUND", message = "El usuario no existe en este tenant OT." });
        }

        var now = DateTimeOffset.UtcNow;

        var existing = await db.UserTempSuspensions
            .Where(s => s.UserId == userId && s.TenantId == tenantId && s.DeletedAt == null && s.EndsAt >= now)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var s in existing)
        {
            s.DeletedAt = now;
            s.DeletedBy = callerId;
        }

        var suspension = new UserTempSuspension
        {
            TenantId = tenantId,
            UserId = userId,
            StartsAt = now,
            EndsAt = request.EndsAt.ToUniversalTime(),
            Reason = request.Reason,
            CreatedAt = now,
            CreatedBy = callerId,
        };

        db.UserTempSuspensions.Add(suspension);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Created($"/api/v1/admin/ot/users/{userId}/suspend", new { id = suspension.Id });
    }

    private static async Task<IResult> UnsuspendUserAsync(
        Guid userId,
        HttpContext httpContext,
        FlitDbContext db,
        [FromQuery] Guid? transitOfficeId,
        CancellationToken cancellationToken)
    {
        var (tenantId, scopeError) = await ResolveOtUserScopeAsync(
            httpContext.User, transitOfficeId, db, cancellationToken).ConfigureAwait(false);
        if (scopeError is not null)
        {
            return scopeError;
        }

        var callerId = ResolveUserId(httpContext.User);
        var now = DateTimeOffset.UtcNow;

        var active = await db.UserTempSuspensions
            .Where(s => s.UserId == userId && s.TenantId == tenantId
                     && s.DeletedAt == null && s.StartsAt <= now && s.EndsAt >= now)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (active.Count == 0)
        {
            return Results.NotFound(new { error = "NO_ACTIVE_SUSPENSION", message = "El usuario no tiene una suspensión activa." });
        }

        foreach (var s in active)
        {
            s.DeletedAt = now;
            s.DeletedBy = callerId;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>Código del único rol de tenant OT — ver <c>TransitOfficeTenantWriteRepository.OtAdminRoleCode</c>.</summary>
    private const string TransitOfficeTenantWriteRepositoryRoleCode = "ot_admin";

    /// <summary>
    /// Resuelve el tenant OT destino del self-service de usuarios: <c>ot_admin</c>
    /// siempre usa su propio tenant (claim JWT, ignora <paramref name="transitOfficeId"/>);
    /// SuperAdmin debe indicar <paramref name="transitOfficeId"/> (oficina del catálogo)
    /// y se resuelve el tenant OT que la tiene vinculada vía
    /// <c>admin.transit_office_profiles</c>.
    /// </summary>
    private static async Task<(Guid TenantId, IResult? Error)> ResolveOtUserScopeAsync(
        ClaimsPrincipal user,
        Guid? transitOfficeId,
        FlitDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(user, out var callerTenantId))
        {
            return (Guid.Empty, Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized));
        }

        if (!IsSuperAdmin(user))
        {
            return (callerTenantId, null);
        }

        if (transitOfficeId is null || transitOfficeId == Guid.Empty)
        {
            return (Guid.Empty, Results.Json(
                new { error = "transitOfficeId es obligatorio para SuperAdmin." },
                statusCode: StatusCodes.Status400BadRequest));
        }

        var targetTenantId = await db.TransitOfficeProfiles
            .AsNoTracking()
            .Where(p => p.TransitOfficeId == transitOfficeId.Value)
            .Select(p => p.TenantId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (targetTenantId == Guid.Empty)
        {
            return (Guid.Empty, Results.NotFound(
                new { error = "No existe un tenant OT vinculado a ese transitOfficeId." }));
        }

        return (targetTenantId, null);
    }

    private sealed record InviteOtUserRequest(string Email, string? FullName);

    private sealed record SuspendOtUserRequest(string Reason, DateTimeOffset EndsAt);

    private sealed record OtUserDto(
        string Id,
        string FullName,
        string Email,
        string? Role,
        string? RoleCode,
        Guid? RoleId,
        string Status,
        DateTimeOffset? CreatedAt,
        bool IsSuspended);
}
