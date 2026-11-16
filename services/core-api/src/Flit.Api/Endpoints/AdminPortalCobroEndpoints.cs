using System.Security.Claims;
using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.OtProfile;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #14393 (Feature #14392, Épica #14297) — medios de pago y switches de cobro del portal de Persona Natural.
/// <list type="bullet">
/// <item><c>/api/v1/admin/ot/portal-cobro</c>: el <c>ot_admin</c> trabaja con el organismo de su token y solo mueve el switch del
/// trámite (la gestión da 403); el SuperAdmin envía <c>?transitOfficeId=</c>, obligatorio, y mueve todo.</item>
/// <item><c>/api/v1/admin/plataforma/persona-natural/cobro</c> (SuperAdmin): sin <c>transitOfficeId</c> es la web de persona (no
/// ofrece PIN); con él, el portal de ese organismo.</item>
/// </list>
/// Errores <c>{ code, message }</c>. Sin caché: lo guardado se ve en la siguiente consulta.
/// </summary>
public static class AdminPortalCobroEndpoints
{
    public const string BasePathOt = "/api/v1/admin/ot/portal-cobro";
    public const string BasePathPlataforma = "/api/v1/admin/plataforma/persona-natural/cobro";

    public sealed record MedioCobroRequest(string? Method, bool? Enabled);

    public sealed record SwitchCobroRequest(Guid? TipoTramiteId, string? Method, string? Charge, bool? Enabled);

    public static IEndpointRouteBuilder MapAdminPortalCobroEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        Mapear(app.MapGroup(BasePathOt).RequireAuthorization(AdminAuthorization.OtAdminOrSuperAdminPolicy).WithTags("Admin · OT · Cobro del portal"), "AdminOtPortalCobro", false);
        Mapear(app.MapGroup(BasePathPlataforma).RequireAuthorization(AdminAuthorization.SuperAdminPolicy).WithTags("Admin · Plataforma · Cobro del portal"), "AdminPlataformaPortalCobro", true);
        return app;
    }

    private static void Mapear(RouteGroupBuilder group, string nombre, bool plataforma)
    {
        group.MapGet("", async (HttpContext http, [FromServices] LeerCobroPnHandler handler, [FromServices] IOtProfileRepository perfiles,
                CancellationToken ct, [FromQuery] Guid? transitOfficeId = null) =>
            {
                var (alcance, error) = await ResolverAlcanceAsync(http.User, transitOfficeId, plataforma, perfiles, ct).ConfigureAwait(false);
                return error ?? Results.Ok(await handler.HandleAsync(alcance!, ct).ConfigureAwait(false));
            })
            .WithName(nombre + "Get")
            .WithSummary("Banderas de los medios y, por tipo y medio, los switches de trámite y gestión con su monto")
            .Produces<CobroPortalView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/medios", async (MedioCobroRequest? body, HttpContext http, [FromServices] GuardarMedioCobroPnHandler handler,
                [FromServices] IOtProfileRepository perfiles, CancellationToken ct, [FromQuery] Guid? transitOfficeId = null) =>
            {
                var (alcance, error) = await ResolverAlcanceAsync(http.User, transitOfficeId, plataforma, perfiles, ct).ConfigureAwait(false);
                if (error is not null)
                    return error;
                if (AdminCompaniesDomainEndpoints.ResolveUserId(http.User) is not { } actor)
                    return Error(StatusCodes.Status401Unauthorized, "usuario_no_identificado", "No se pudo identificar al usuario.");
                if (body?.Enabled is not { } enabled)
                    return Error(StatusCodes.Status400BadRequest, CobroPnErrores.MedioInvalido, "Indique si el medio queda habilitado (enabled).");
                var falla = await handler.HandleAsync(alcance!, body.Method, enabled, actor, ct).ConfigureAwait(false);
                return falla is null ? Results.NoContent() : Falla(falla);
            })
            .WithName(nombre + "PutMedio")
            .WithSummary("Habilita o deshabilita un medio de pago en el portal; no toca otros portales")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/switches", async (SwitchCobroRequest? body, HttpContext http, [FromServices] GuardarSwitchCobroPnHandler handler,
                [FromServices] IOtProfileRepository perfiles, CancellationToken ct, [FromQuery] Guid? transitOfficeId = null) =>
            {
                var (alcance, error) = await ResolverAlcanceAsync(http.User, transitOfficeId, plataforma, perfiles, ct).ConfigureAwait(false);
                if (error is not null)
                    return error;
                if (AdminCompaniesDomainEndpoints.ResolveUserId(http.User) is not { } actor)
                    return Error(StatusCodes.Status401Unauthorized, "usuario_no_identificado", "No se pudo identificar al usuario.");
                if (body?.Enabled is not { } enabled)
                    return Error(StatusCodes.Status400BadRequest, CobroPnErrores.CobroInvalido, "Indique si el switch queda encendido (enabled).");
                var falla = await handler.HandleAsync(alcance!, body.TipoTramiteId, body.Method, body.Charge, enabled, actor, ct).ConfigureAwait(false);
                return falla is null ? Results.NoContent() : Falla(falla);
            })
            .WithName(nombre + "PutSwitch")
            .WithSummary("Enciende o apaga el cobro del trámite o de la gestión para un tipo y un medio; encender exige monto mayor que cero")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Ruta del OT: SuperAdmin con <c>transitOfficeId</c> obligatorio; otro rol, el organismo de su perfil (token), y un
    /// identificador ajeno da 403 sin ejecutar nada. Ruta de Plataforma: <c>transitOfficeId</c> opcional (ausente = la web).
    /// </summary>
    private static async Task<(AlcanceCobroPn? Alcance, IResult? Error)> ResolverAlcanceAsync(
        ClaimsPrincipal user, Guid? transitOfficeId, bool plataforma, IOtProfileRepository perfiles, CancellationToken ct)
    {
        var pedido = transitOfficeId is { } t && t != Guid.Empty ? t : (Guid?)null;
        if (plataforma)
            return (new AlcanceCobroPn(pedido, true), null);

        if (EsSuperAdmin(user))
        {
            return pedido is { } organismo
                ? (new AlcanceCobroPn(organismo, true), null)
                : (null, Error(StatusCodes.Status400BadRequest, CobroPnErrores.OrganismoRequerido, "Indique el organismo (transitOfficeId)."));
        }

        if (!RequestTenantResolver.TryResolveTenantId(user, out var tenantId)
            || await perfiles.GetByTenantAsync(tenantId, ct).ConfigureAwait(false) is not { } perfil)
            return (null, Error(StatusCodes.Status403Forbidden, CobroPnErrores.SinOrganismo, "Su usuario no tiene un organismo de tránsito asociado."));

        if (pedido is { } enviado && enviado != perfil.TransitOfficeId)
            return (null, Error(StatusCodes.Status403Forbidden, CobroPnErrores.OtroOrganismo, "No puede consultar ni cambiar el cobro de otro organismo."));

        return (new AlcanceCobroPn(perfil.TransitOfficeId, false), null);
    }

    private static IResult Falla(string codigo) => codigo switch
    {
        CobroPnErrores.GestionAjena => Error(StatusCodes.Status403Forbidden, codigo, "El cobro de la gestión lo define FLIT: su organismo no puede cambiarlo."),
        CobroPnErrores.OtroOrganismo => Error(StatusCodes.Status403Forbidden, codigo, "No puede consultar ni cambiar el cobro de otro organismo."),
        CobroPnErrores.TipoNoExiste or CobroPnErrores.OrganismoNoExiste => Error(StatusCodes.Status404NotFound, codigo, "El tipo de trámite o el organismo no existe."),
        CobroPnErrores.PinEnWeb => Error(StatusCodes.Status400BadRequest, codigo, "El PIN de caja no existe en la web de persona: solo se ofrece en el punto del organismo."),
        CobroPnErrores.SinMonto => Error(
            StatusCodes.Status400BadRequest, codigo, "No se puede encender: ese concepto no tiene un monto mayor que cero para el tipo y el organismo. Fíjelo primero en Precios."),
        CobroPnErrores.MedioInvalido => Error(StatusCodes.Status400BadRequest, codigo, "Medio de pago inválido: use PASARELA o PIN."),
        CobroPnErrores.CobroInvalido => Error(StatusCodes.Status400BadRequest, codigo, "Concepto de cobro inválido: use TRAMITE o GESTION."),
        CobroPnErrores.TipoObligatorio => Error(StatusCodes.Status400BadRequest, codigo, "Indique el tipo de trámite."),
        _ => Error(StatusCodes.Status400BadRequest, codigo, "No se pudo guardar el cobro."),
    };

    private static IResult Error(int status, string code, string message) => Results.Json(new { code, message }, statusCode: status);

    private static bool EsSuperAdmin(ClaimsPrincipal user) =>
        user.IsInRole(AdminAuthorization.SuperAdminRole)
        || string.Equals(user.FindFirstValue(AdminAuthorization.RoleClaimType), AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase);
}
