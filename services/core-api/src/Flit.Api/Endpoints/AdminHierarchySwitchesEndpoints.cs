using System.Security.Claims;
using Flit.Admin.Application.Auditing;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Auditing;
using Flit.Queries.Domain.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// SuperAdmin — Plataforma → interruptores globales de la jerarquía de clientes
/// (HU #12323, Feature #12254, ADR-0057). Permiten apagar/encender <c>group_read_scope</c> e
/// <c>inherited_configuration</c> sin despliegue: la siguiente petición ya ve el nuevo estado
/// (lectura por petición, sin caché, sin nuevo token). Cada PUT queda en el rastro de auditoría
/// administrativo (<see cref="AdminAuditFilter"/>). No expone ni modifica la jerarquía en sí.
/// </summary>
public static class AdminHierarchySwitchesEndpoints
{
    private const string EntityName = "hierarchy_switch";

    public static IEndpointRouteBuilder MapAdminHierarchySwitchesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/platform/hierarchy-switches")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Plataforma · Jerarquía de clientes");

        group.MapGet("", ListAsync)
            .WithName("AdminHierarchySwitchesList")
            .Produces<HierarchySwitchesListResponse>(StatusCodes.Status200OK);

        group.MapPut("/{key}", SetAsync)
            .WithName("AdminHierarchySwitchesSet")
            .Produces<HierarchySwitchResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .AddEndpointFilter(new AdminAuditFilter(
                AuditVocabulary.Modules.Config, AuditVocabulary.Operations.Update, EntityName));

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] IHierarchySwitches switches,
        CancellationToken cancellationToken)
    {
        var items = await switches.ListAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(new HierarchySwitchesListResponse(items.Select(ToResponse).ToList()));
    }

    private static async Task<IResult> SetAsync(
        string key,
        [FromBody] SetHierarchySwitchRequest? request,
        HttpContext httpContext,
        [FromServices] IHierarchySwitches switches,
        CancellationToken cancellationToken)
    {
        if (request?.IsEnabled is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["isEnabled"] = ["Debe indicar isEnabled (true/false)."],
            });
        }

        if (string.IsNullOrWhiteSpace(key) || key.Length > 64)
            return Results.NotFound(new { code = "HIERARCHY_SWITCH_NOT_FOUND" });

        var state = await switches
            .SetAsync(key, request.IsEnabled.Value, ResolveActorUserId(httpContext.User), cancellationToken)
            .ConfigureAwait(false);

        return state is null
            ? Results.NotFound(new { code = "HIERARCHY_SWITCH_NOT_FOUND" })
            : Results.Ok(ToResponse(state));
    }

    private static HierarchySwitchResponse ToResponse(HierarchySwitchState state) =>
        new(state.Key, state.IsEnabled, state.UpdatedAt, state.UpdatedBy);

    /// <summary>Actor del cambio (claim <c>sub</c>); no toca el tenant — eso es de <see cref="RequestTenantResolver"/>.</summary>
    private static Guid? ResolveActorUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

/// <summary>Body de <c>PUT /api/v1/admin/platform/hierarchy-switches/{key}</c>.</summary>
public sealed record SetHierarchySwitchRequest(bool? IsEnabled);

/// <summary>Estado de un interruptor de jerarquía.</summary>
public sealed record HierarchySwitchResponse(string Key, bool IsEnabled, DateTimeOffset UpdatedAt, Guid? UpdatedBy);

/// <summary>Lista de interruptores de jerarquía.</summary>
public sealed record HierarchySwitchesListResponse(IReadOnlyList<HierarchySwitchResponse> Items);
