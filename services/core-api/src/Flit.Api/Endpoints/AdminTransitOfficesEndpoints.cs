using System.Security.Claims;
using Flit.Admin.Application.Companies.TransitOffices.ListTransitOfficesOperationalStatus;
using Flit.Admin.Application.Companies.TransitOffices.SearchTransitOffices;
using Flit.Admin.Application.Companies.TransitOffices.UpdateTransitOfficeQuipuxSettings;
using Flit.Admin.Domain.OtProfile;
using Flit.Api.Authorization;
using Flit.Modules.Quipux.Application.UseCases.Cola;
using Flit.Modules.Quipux.Domain.Consola;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints del catálogo de organismos de tránsito (HU #10192, RF13, AC1). La búsqueda
/// exige rol SuperAdmin u ot_admin (módulo OT — HU #10218 / #10236); el estado operativo
/// (RF01) y la parametrización Quipux del catálogo (HU #10710) son exclusivos de SuperAdmin.
/// El catálogo se lee desde <c>catalogs.transit_offices</c> (BD).
/// </summary>
public static class AdminTransitOfficesEndpoints
{
    public static IEndpointRouteBuilder MapAdminTransitOfficesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/transit-offices")
            .RequireAuthorization(AdminAuthorization.OtModulePolicy)
            .WithTags("Admin · Compañías");

        // GET /api/v1/admin/transit-offices?search= — catálogo con búsqueda opcional (#10192 AC1).
        group.MapGet("", SearchTransitOfficesAsync)
            .WithName("AdminTransitOfficesSearch")
            .WithSummary("Busca en el catálogo de Organismos de Tránsito")
            .WithDescription("Catálogo de Organismos de Tránsito (catalogs.transit_offices) con búsqueda "
                + "opcional por nombre/código vía el parámetro search. Requiere SuperAdmin u ot_admin.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // GET /api/v1/admin/transit-offices/operational-status — estado operativo por OT (RF01).
        // Exclusivo de SuperAdmin: añade la policy SuperAdmin sobre la del grupo (OtModule), de
        // modo que ot_admin —que sí ve el catálogo— queda fuera de la gestión del ciclo de vida.
        group.MapGet("/operational-status", ListOperationalStatusAsync)
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithName("AdminTransitOfficesOperationalStatus")
            .WithSummary("Estado operativo de los Organismos de Tránsito")
            .WithDescription("Por cada oficina del catálogo devuelve si tiene tenant OT dado de alta y, "
                + "si lo tiene, su estado activo/inactivo y modo de operación (join catalogs.transit_offices "
                + "+ admin.transit_office_profiles + identity.tenants). Requiere SuperAdmin.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // PUT /api/v1/admin/transit-offices/{id}/quipux-settings — parametrización Quipux de la
        // secretaría DESTINO (HU #10710). Exclusivo de SuperAdmin, igual que el estado operativo:
        // el catálogo es global y su carga es manual, secretaría por secretaría.
        group.MapPut("/{id:guid}/quipux-settings", UpdateQuipuxSettingsAsync)
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithName("AdminTransitOfficesUpdateQuipuxSettings")
            .WithSummary("Parametriza la radicación Quipux de una secretaría (código DIVIPO + banderas)")
            .WithDescription("Reemplaza divipo_code y las tres banderas por familia de trámite "
                + "(matrícula/traspaso/otros) de catalogs.transit_offices. divipoCode nulo o vacío "
                + "significa «aún no se conoce» — no es un error: es el estado normal de las "
                + "secretarías todavía no integradas, y deja a la secretaría NO elegible para "
                + "radicar. Activar banderas sin DIVIPO se permite (el alta es gradual) pero la "
                + "respuesta devuelve elegible=false. No confundir con "
                + "admin.transit_office_profiles.operation_mode, que describe al OT-CLIENTE. "
                + "Requiere SuperAdmin.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // GET /api/v1/admin/transit-offices/{id}/quipux-submissions — cola Quipux de la secretaría
        // destino (HU #10774): las radicaciones (activas + histórico) de los trámites cuyo
        // transit_office_id es este OT, paginadas. SuperAdmin (cualquier secretaría) u ot_admin
        // (acotado a la suya por EnforceQuipuxColaScope). Hereda OtModulePolicy del grupo.
        group.MapGet("/{id:guid}/quipux-submissions", ListQuipuxColaAsync)
            .WithName("AdminTransitOfficesListQuipuxCola")
            .WithSummary("Cola de radicaciones Quipux de una secretaría (activa + histórico)")
            .WithDescription("Lista tramites.quipux_submissions de los trámites radicados a esta "
                + "secretaría destino, con el número de referencia, tipo de trámite y cliente. "
                + "Incluye todos los estados (pendiente/registrado/aprobado/rechazado/fallido) "
                + "ordenados del más nuevo al más viejo. Requiere SuperAdmin u ot_admin; el "
                + "ot_admin solo puede consultar su propia secretaría.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // POST .../{submissionId}/retry — re-encolar una radicación 'fallido' (dead-letter).
        // SuperAdmin (cualquier secretaría) u ot_admin acotado a la suya (EnforceQuipuxColaScope).
        group.MapPost("/{id:guid}/quipux-submissions/{submissionId:guid}/retry", RetryQuipuxSubmissionAsync)
            .WithName("AdminTransitOfficesRetryQuipuxSubmission")
            .WithSummary("Re-encola una radicación Quipux fallida")
            .WithDescription("Lleva una submission 'fallido' de vuelta a 'pendiente' con presupuesto "
                + "de intentos fresco, de modo que el registrador la vuelva a reclamar. 409 si no "
                + "está en 'fallido' (invalid-state) o si el trámite ya tiene otra radicación viva "
                + "(conflict). Requiere SuperAdmin u ot_admin; el ot_admin solo su propia secretaría.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        // POST .../{submissionId}/cancel — cancelar una radicación 'pendiente' antes de radicarla.
        // SuperAdmin (cualquier secretaría) u ot_admin acotado a la suya (EnforceQuipuxColaScope).
        group.MapPost("/{id:guid}/quipux-submissions/{submissionId:guid}/cancel", CancelQuipuxSubmissionAsync)
            .WithName("AdminTransitOfficesCancelQuipuxSubmission")
            .WithSummary("Cancela una radicación Quipux pendiente")
            .WithDescription("Lleva una submission 'pendiente' a 'fallido' terminal antes de que se "
                + "radique. 409 si ya no está en 'pendiente' (p. ej. un worker la registró entre la "
                + "carga y el clic). Requiere SuperAdmin u ot_admin; el ot_admin solo su propia secretaría.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> SearchTransitOfficesAsync(
        [FromServices] SearchTransitOfficesHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] string? search = null)
    {
        var result = await handler
            .HandleAsync(new SearchTransitOfficesQuery { Search = search }, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> ListOperationalStatusAsync(
        [FromServices] ListTransitOfficesOperationalStatusHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new ListTransitOfficesOperationalStatusQuery(), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateQuipuxSettingsAsync(
        Guid id,
        UpdateTransitOfficeQuipuxSettingsRequest request,
        [FromServices] UpdateTransitOfficeQuipuxSettingsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(
                new UpdateTransitOfficeQuipuxSettingsCommand
                {
                    TransitOfficeId = id,
                    Request = request,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return result.Status switch
        {
            UpdateTransitOfficeQuipuxSettingsStatus.NotFound =>
                Results.NotFound(new { code = "TRANSIT_OFFICE_NOT_FOUND" }),
            UpdateTransitOfficeQuipuxSettingsStatus.InvalidDivipoCode => Results.Json(
                new { code = "DIVIPO_CODE_INVALID" },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            UpdateTransitOfficeQuipuxSettingsStatus.MissingFlags => Results.Json(
                new { code = "QUIPUX_FLAGS_REQUIRED" },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Ok(result.Settings),
        };
    }

    private static async Task<IResult> ListQuipuxColaAsync(
        Guid id,
        ClaimsPrincipal user,
        [FromServices] ListarColaQuipuxHandler handler,
        [FromServices] IOtProfileRepository profileRepository,
        CancellationToken cancellationToken,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var forbidden = await EnforceQuipuxColaScopeAsync(user, id, profileRepository, cancellationToken)
            .ConfigureAwait(false);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var result = await handler
            .HandleAsync(new ListarColaQuipuxQuery(id, page, pageSize), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> RetryQuipuxSubmissionAsync(
        Guid id,
        Guid submissionId,
        ClaimsPrincipal user,
        [FromServices] ReintentarSubmissionHandler handler,
        [FromServices] IOtProfileRepository profileRepository,
        CancellationToken cancellationToken)
    {
        var forbidden = await EnforceQuipuxColaScopeAsync(user, id, profileRepository, cancellationToken)
            .ConfigureAwait(false);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var status = await handler
            .HandleAsync(new ReintentarSubmissionCommand(submissionId, id, ResolveUserId(user)), cancellationToken)
            .ConfigureAwait(false);

        return MapManualOpResult(status);
    }

    private static async Task<IResult> CancelQuipuxSubmissionAsync(
        Guid id,
        Guid submissionId,
        ClaimsPrincipal user,
        [FromServices] CancelarSubmissionHandler handler,
        [FromServices] IOtProfileRepository profileRepository,
        CancellationToken cancellationToken)
    {
        var forbidden = await EnforceQuipuxColaScopeAsync(user, id, profileRepository, cancellationToken)
            .ConfigureAwait(false);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var status = await handler
            .HandleAsync(new CancelarSubmissionCommand(submissionId, id, ResolveUserId(user)), cancellationToken)
            .ConfigureAwait(false);

        return MapManualOpResult(status);
    }

    /// <summary>
    /// Acota una operación de la cola Quipux al organismo del llamante (HU #10774). El SuperAdmin
    /// es cross-tenant: opera la cola de cualquier secretaría. Un ot_admin queda restringido a la
    /// SUYA: como en el catálogo <c>tenant_id</c> ≠ <c>transit_office_id</c> (son columnas
    /// distintas de <c>admin.transit_office_profiles</c>), se resuelve el organismo del ot_admin
    /// desde su tenant vía el perfil persistido y se exige que el <paramref name="transitOfficeId"/>
    /// de la ruta coincida con ese <c>transit_office_id</c>. Devuelve <c>null</c> si el acceso es
    /// válido; en caso contrario, el 403 a retornar.
    /// </summary>
    private static async Task<IResult?> EnforceQuipuxColaScopeAsync(
        ClaimsPrincipal user,
        Guid transitOfficeId,
        IOtProfileRepository profileRepository,
        CancellationToken cancellationToken)
    {
        if (user.IsInRole(AdminAuthorization.SuperAdminRole))
        {
            return null;
        }

        if (Guid.TryParse(user.FindFirstValue(AdminAuthorization.TenantIdClaimType), out var tenantId))
        {
            var profile = await profileRepository
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

    /// <summary>Traduce el desenlace de una acción manual de la cola a una respuesta HTTP.</summary>
    private static IResult MapManualOpResult(QuipuxManualOpStatus status) => status switch
    {
        QuipuxManualOpStatus.Ok => Results.Ok(new { code = "OK" }),
        QuipuxManualOpStatus.NotFound => Results.NotFound(new { code = "QUIPUX_SUBMISSION_NOT_FOUND" }),
        QuipuxManualOpStatus.InvalidState => Results.Json(
            new { code = "QUIPUX_SUBMISSION_INVALID_STATE" },
            statusCode: StatusCodes.Status409Conflict),
        QuipuxManualOpStatus.Conflict => Results.Json(
            new { code = "QUIPUX_SUBMISSION_CONFLICT" },
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Json(new { code = "QUIPUX_SUBMISSION_ERROR" }, statusCode: StatusCodes.Status500InternalServerError),
    };

    /// <summary>Id del actor desde el token (<c>sub</c> o NameIdentifier). Null si no viene o no parsea.</summary>
    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }
}
