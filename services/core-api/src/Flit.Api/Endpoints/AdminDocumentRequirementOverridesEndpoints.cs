using System.Security.Claims;
using Flit.Admin.Application.DocumentRequirementOverrides;
using Flit.Admin.Application.DocumentRequirementOverrides.ListDocumentRequirementOverrides;
using Flit.Admin.Application.DocumentRequirementOverrides.SetDocumentRequirementOverride;
using Flit.Admin.Domain.OtProfile;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de la obligatoriedad documental por Organismo de Tránsito (HU #10198; HU #10881).
/// Granular solo para OT: cada documento asociado a un trámite puede marcarse, por OT, como
/// obligatorio (REQUIRED), opcional (OPTIONAL) o no aplica (NOT_APPLICABLE → se oculta de la
/// matriz de ese OT). El upsert por tupla natural usa estado=DEFAULT para limpiar el override.
/// El grupo exige SuperAdmin u ot_admin (<see cref="AdminAuthorization.OtModulePolicy"/>); un
/// ot_admin queda acotado a SU propio organismo de tránsito vía
/// <see cref="EnforceTransitOfficeScopeAsync"/> (mismo guard que la cola Quipux, HU #10774):
/// se resuelve el <c>transit_office_id</c> del llamante a partir del claim <c>tenant_id</c>
/// (<see cref="IOtProfileRepository.GetByTenantAsync"/>) y se exige que coincida con el
/// <c>transitOfficeId</c> de la petición. El SuperAdmin es cross-tenant (cualquier OT).
/// </summary>
public static class AdminDocumentRequirementOverridesEndpoints
{
    public static IEndpointRouteBuilder MapAdminDocumentRequirementOverridesEndpoints(
        this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/document-requirement-overrides")
            .RequireAuthorization(AdminAuthorization.OtModulePolicy)
            .WithTags("Admin · Órdenes documentales");

        // GET ?procedureTypeId&transitOfficeId — overrides de obligatoriedad del trámite/OT.
        group.MapGet("/", ListAsync)
            .WithName("AdminDocumentRequirementOverrideList")
            .WithSummary("Lista la obligatoriedad por OT de un trámite")
            .WithDescription("Retorna los overrides de obligatoriedad (REQUIRED / OPTIONAL / NOT_APPLICABLE) "
                + "configurados para un trámite en un Organismo de Tránsito. Requiere procedureTypeId y "
                + "transitOfficeId; 400 si falta alguno. Requiere SuperAdmin u ot_admin (acotado a su propia "
                + "OT; 403 si consulta otra).")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // PUT — upsert por tupla natural (estado=DEFAULT limpia) → 200 / 204 / 422 / 404.
        group.MapPut("/", SetAsync)
            .WithName("AdminDocumentRequirementOverrideSet")
            .WithSummary("Define o limpia la obligatoriedad de un documento por OT")
            .WithDescription("Upsert por tupla natural (trámite, documento, OT). estado=REQUIRED/OPTIONAL/"
                + "NOT_APPLICABLE fija el override (200); estado=DEFAULT lo limpia y devuelve 204. "
                + "404 si trámite/documento/OT no existen; 422 si el payload es inválido. Requiere SuperAdmin "
                + "u ot_admin (acotado a su propia OT; 403 si intenta configurar otra).")
            .Produces<DocumentRequirementOverrideResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        [FromServices] ListDocumentRequirementOverridesHandler handler,
        [FromServices] IOtProfileRepository otProfileRepository,
        CancellationToken cancellationToken,
        [FromQuery] Guid? procedureTypeId = null,
        [FromQuery] Guid? transitOfficeId = null)
    {
        if (procedureTypeId is null || procedureTypeId == Guid.Empty)
        {
            return BadRequest("El parámetro procedureTypeId es obligatorio.");
        }

        if (transitOfficeId is null || transitOfficeId == Guid.Empty)
        {
            return BadRequest("El parámetro transitOfficeId es obligatorio.");
        }

        var forbidden = await EnforceTransitOfficeScopeAsync(
            httpContext.User, transitOfficeId.Value, otProfileRepository, cancellationToken)
            .ConfigureAwait(false);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var result = await handler
            .HandleAsync(
                new ListDocumentRequirementOverridesQuery
                {
                    ProcedureTypeId = procedureTypeId.Value,
                    TransitOfficeId = transitOfficeId.Value,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> SetAsync(
        SetDocumentRequirementOverrideRequest request,
        HttpContext httpContext,
        [FromServices] SetDocumentRequirementOverrideHandler handler,
        [FromServices] IOtProfileRepository otProfileRepository,
        CancellationToken cancellationToken)
    {
        var forbidden = await EnforceTransitOfficeScopeAsync(
            httpContext.User, request.TransitOfficeId, otProfileRepository, cancellationToken)
            .ConfigureAwait(false);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var command = new SetDocumentRequirementOverrideCommand
        {
            Request = request,
            Actor = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            SetDocumentRequirementOverrideOutcome.Set => Results.Ok(result.Response),
            SetDocumentRequirementOverrideOutcome.Cleared => Results.NoContent(),
            SetDocumentRequirementOverrideOutcome.ValidationFailed => Results.Json(
                new ErrorResponse(result.Error!), statusCode: StatusCodes.Status422UnprocessableEntity),
            SetDocumentRequirementOverrideOutcome.ProcedureTypeNotFound => Results.NotFound(
                new ErrorResponse($"No existe el tipo de trámite {request.ProcedureTypeId}.")),
            SetDocumentRequirementOverrideOutcome.DocumentTypeNotFound => Results.NotFound(
                new ErrorResponse($"No existe el tipo de documento {request.DocumentTypeId}.")),
            _ => Results.NotFound(
                new ErrorResponse($"No existe el organismo de tránsito {request.TransitOfficeId}.")),
        };
    }

    private static IResult BadRequest(string message) =>
        Results.Json(new ErrorResponse(message), statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// Acota el override al organismo del llamante (HU #10881). El SuperAdmin es cross-tenant:
    /// consulta/configura overrides de cualquier OT. Un ot_admin queda restringido al SUYO:
    /// como en el catálogo <c>tenant_id</c> ≠ <c>transit_office_id</c> (son columnas distintas
    /// de <c>admin.transit_office_profiles</c>), se resuelve el organismo del ot_admin desde su
    /// tenant vía el perfil persistido y se exige que el <paramref name="transitOfficeId"/> de
    /// la petición coincida con ese <c>transit_office_id</c>. NUNCA se confía en el parámetro
    /// que envía el cliente para resolver la identidad del ot_admin — solo para compararlo
    /// contra lo resuelto desde el claim <c>tenant_id</c> del token. Devuelve <c>null</c> si el
    /// acceso es válido; en caso contrario, el 403 a retornar (mismo guard que la cola Quipux,
    /// HU #10774, <see cref="AdminTransitOfficesEndpoints"/>).
    /// </summary>
    private static async Task<IResult?> EnforceTransitOfficeScopeAsync(
        ClaimsPrincipal user,
        Guid transitOfficeId,
        IOtProfileRepository otProfileRepository,
        CancellationToken cancellationToken)
    {
        if (user.IsInRole(AdminAuthorization.SuperAdminRole))
        {
            return null;
        }

        if (Guid.TryParse(user.FindFirstValue(AdminAuthorization.TenantIdClaimType), out var tenantId))
        {
            var profile = await otProfileRepository
                .GetByTenantAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            if (profile is not null && profile.TransitOfficeId == transitOfficeId)
            {
                return null;
            }
        }

        return Results.Json(
            new { code = "TRANSIT_OFFICE_FORBIDDEN" },
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Cuerpo de error simple: <c>{ error: "mensaje" }</c> (400 / 422 / 404).</summary>
    private sealed record ErrorResponse(string Error);
}
