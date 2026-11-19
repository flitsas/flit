using System.Security.Claims;
using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.OtProfile;
using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #14388 (Feature #14387, Épica #14297) — precios del portal de Persona Natural vistos desde el dashboard del organismo:
/// <c>/api/v1/admin/ot/portal-tarifas</c>. Opera sobre <c>admin.persona_natural_tariffs</c> con el mismo caso de uso de
/// Plataforma, acotado al organismo. El <c>ot_admin</c> trabaja con el organismo de su token y solo con derechos de tránsito y
/// las transformaciones; el SuperAdmin envía <c>?transitOfficeId=</c> y escribe todos los conceptos. Los valores generales
/// (organismo nulo) se ven pero no se editan aquí. Los endpoints de Plataforma no cambian. Errores: <c>{ code, message }</c>.
/// </summary>
public static class AdminOtPortalTarifasEndpoints
{
    public const string BasePath = "/api/v1/admin/ot/portal-tarifas";

    public sealed record VersionarTarifasOtRequest(
        Guid? TipoTramiteId, DateOnly? VigenteDesde, IReadOnlyList<AdminPlataformaPersonaNaturalEndpoints.ValorTarifaRequest>? Valores, Guid? OrganismoId = null);

    public static IEndpointRouteBuilder MapAdminOtPortalTarifasEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup(BasePath)
            .RequireAuthorization(AdminAuthorization.OtAdminOrSuperAdminPolicy)
            .WithTags("Admin · OT · Precios del portal");

        group.MapGet("", ListarAsync)
            .WithName("AdminOtPortalTarifasList")
            .WithSummary("Tarifas del organismo y los valores generales que le aplican (estos, no editables)")
            .Produces<IReadOnlyList<TarifaOtView>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/tipos", TiposAsync)
            .WithName("AdminOtPortalTarifasTipos")
            .WithSummary("Tipos de trámite del portal, para el formulario de precios")
            .Produces<IReadOnlyList<TipoTarifaPnView>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}/historial", HistorialAsync)
            .WithName("AdminOtPortalTarifasHistorial")
            .WithSummary("Versiones de un ámbito, de la más reciente a la más antigua, con quién hizo cada cambio")
            .Produces<IReadOnlyList<TarifaOtView>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("", VersionarAsync)
            .WithName("AdminOtPortalTarifasVersionar")
            .WithSummary("Registra una versión de precio para el organismo; el mismo día reemplaza")
            .Produces<IReadOnlyList<TarifaOtView>>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> ListarAsync(
        HttpContext http,
        [FromServices] ListarTarifasOtHandler handler,
        [FromServices] IOtProfileRepository perfiles,
        CancellationToken ct,
        [FromQuery] Guid? transitOfficeId = null,
        [FromQuery] bool? soloVigentes = null,
        [FromQuery] Guid? tipoTramiteId = null,
        [FromQuery] string? concepto = null,
        [FromQuery] DateOnly? fecha = null)
    {
        var (alcance, error) = await ResolverAlcanceAsync(http.User, transitOfficeId, null, perfiles, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        var r = await handler.HandleAsync(alcance!, soloVigentes ?? false, tipoTramiteId, concepto, fecha, ct).ConfigureAwait(false);
        return r.Error is null ? Results.Ok(r.Tarifas) : Falla(r.Error);
    }

    private static async Task<IResult> TiposAsync(
        HttpContext http,
        [FromServices] TiposTarifaOtHandler handler,
        [FromServices] IOtProfileRepository perfiles,
        CancellationToken ct,
        [FromQuery] Guid? transitOfficeId = null)
    {
        var (_, error) = await ResolverAlcanceAsync(http.User, transitOfficeId, null, perfiles, ct).ConfigureAwait(false);
        return error ?? Results.Ok(await handler.HandleAsync(ct).ConfigureAwait(false));
    }

    private static async Task<IResult> HistorialAsync(
        Guid id,
        HttpContext http,
        [FromServices] HistorialTarifaOtHandler handler,
        [FromServices] IOtProfileRepository perfiles,
        CancellationToken ct,
        [FromQuery] Guid? transitOfficeId = null)
    {
        var (alcance, error) = await ResolverAlcanceAsync(http.User, transitOfficeId, null, perfiles, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        var r = await handler.HandleAsync(alcance!, id, ct).ConfigureAwait(false);
        return r.Error is null ? Results.Ok(r.Tarifas) : Falla(r.Error);
    }

    private static async Task<IResult> VersionarAsync(
        VersionarTarifasOtRequest body,
        HttpContext http,
        [FromServices] VersionarTarifasOtHandler handler,
        [FromServices] IOtProfileRepository perfiles,
        CancellationToken ct,
        [FromQuery] Guid? transitOfficeId = null)
    {
        var (alcance, error) = await ResolverAlcanceAsync(http.User, transitOfficeId, body?.OrganismoId, perfiles, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (AdminCompaniesDomainEndpoints.ResolveUserId(http.User) is not { } actor)
            return Error(StatusCodes.Status401Unauthorized, "usuario_no_identificado", "No se pudo identificar al usuario.");

        var r = await handler.HandleAsync(
            alcance!, body?.TipoTramiteId, body?.Valores?.Select(v => new TarifaPnValor(v.Concepto ?? string.Empty, v.ValorCop)).ToList(),
            body?.VigenteDesde, actor, ct).ConfigureAwait(false);
        return r.Error is null ? Results.Created($"{BasePath}", r.Tarifas) : Falla(r.Error);
    }

    /// <summary>
    /// Resuelve el organismo del llamante. SuperAdmin: el de <c>transitOfficeId</c> (o el del cuerpo), obligatorio. Otro rol: el
    /// del perfil OT de SU tenant, tomado del token; si manda el identificador de OTRO organismo (en la ruta o en el cuerpo)
    /// responde 403 y no se ejecuta nada. NUNCA se confía en lo que envía el cliente para decidir quién es.
    /// </summary>
    private static async Task<(AlcanceTarifasOt? Alcance, IResult? Error)> ResolverAlcanceAsync(
        ClaimsPrincipal user, Guid? transitOfficeId, Guid? organismoDelCuerpo, IOtProfileRepository perfiles, CancellationToken ct)
    {
        var pedido = transitOfficeId is { } t && t != Guid.Empty ? t : organismoDelCuerpo is { } c && c != Guid.Empty ? c : (Guid?)null;
        if (EsSuperAdmin(user))
        {
            return pedido is { } organismo
                ? (new AlcanceTarifasOt(organismo, true), null)
                : (null, Error(StatusCodes.Status400BadRequest, TarifaOtErrores.OrganismoRequerido, "Indique el organismo (transitOfficeId)."));
        }

        if (!RequestTenantResolver.TryResolveTenantId(user, out var tenantId)
            || await perfiles.GetByTenantAsync(tenantId, ct).ConfigureAwait(false) is not { } perfil)
        {
            return (null, Error(StatusCodes.Status403Forbidden, TarifaOtErrores.SinOrganismo, "Su usuario no tiene un organismo de tránsito asociado."));
        }

        foreach (var enviado in new[] { transitOfficeId, organismoDelCuerpo })
        {
            if (enviado is { } id && id != Guid.Empty && id != perfil.TransitOfficeId)
                return (null, Error(StatusCodes.Status403Forbidden, TarifaOtErrores.OtroOrganismo, "No puede consultar ni cambiar los precios de otro organismo."));
        }

        return (new AlcanceTarifasOt(perfil.TransitOfficeId, false), null);
    }

    private static IResult Falla(string codigo) => codigo switch
    {
        TarifaOtErrores.ConceptoAjeno => Error(
            StatusCodes.Status403Forbidden, codigo, "Servicio FLIT y envío del documento los define FLIT: su organismo no puede cambiarlos."),
        TarifaOtErrores.OtroOrganismo => Error(
            StatusCodes.Status403Forbidden, codigo, "No puede consultar ni cambiar los precios de otro organismo."),
        TarifaOtErrores.TipoObligatorio => Error(StatusCodes.Status400BadRequest, codigo, "Indique el tipo de trámite."),
        TarifaPnErrores.NoEncontrada => Error(StatusCodes.Status404NotFound, codigo, "La tarifa no existe."),
        TarifaPnErrores.TipoNoExiste or TarifaPnErrores.OrganismoNoExiste => Error(
            StatusCodes.Status404NotFound, codigo, "El tipo de trámite o el organismo no existe."),
        TarifaPnErrores.Solapada => Error(
            StatusCodes.Status409Conflict, codigo, "Ya existe un precio para ese concepto y tipo que se cruza con esas fechas."),
        _ => Error(StatusCodes.Status400BadRequest, codigo, MensajeValidacion(codigo)),
    };

    private static string MensajeValidacion(string codigo) => codigo switch
    {
        TarifaPnErrores.ValoresVacios => "Indique al menos un concepto con su valor.",
        TarifaPnErrores.ConceptoInvalido => $"Concepto inválido: use {string.Join(", ", ConceptosTarifaPn.Todos)}.",
        TarifaPnErrores.ConceptoRepetido => "Un concepto no puede repetirse en la misma versión.",
        TarifaPnErrores.ValorInvalido => $"Cada valor debe ser un entero en pesos entre 0 y {VersionarTarifasPnHandler.ValorMaximoCop}.",
        TarifaPnErrores.TipoObligatorio => "Derechos de tránsito y servicio FLIT se registran siempre para un tipo de trámite.",
        TarifaPnErrores.FechaInicioInvalida => "Indique la fecha de inicio de la vigencia.",
        TarifaPnErrores.FechaInicioPasada => "La fecha de inicio no puede ser anterior a hoy.",
        _ => "No se pudo registrar el precio.",
    };

    private static IResult Error(int status, string code, string message) =>
        Results.Json(new { code, message }, statusCode: status);

    private static bool EsSuperAdmin(ClaimsPrincipal user) =>
        user.IsInRole(AdminAuthorization.SuperAdminRole)
        || string.Equals(user.FindFirstValue(AdminAuthorization.RoleClaimType), AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase);
}
