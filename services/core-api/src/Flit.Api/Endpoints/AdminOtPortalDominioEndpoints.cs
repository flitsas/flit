using System.Security.Claims;
using Flit.Admin.Application.Companies.TransitOffices.PortalDominio;
using Flit.Admin.Domain.Companies.TransitOffices.PortalDominio;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Dominio del portal Persona Natural de cada organismo (Feature #14380, Épica #14297). Rol <c>ot_admin</c> o SuperAdmin
/// (<see cref="AdminAuthorization.OtAdminOrSuperAdminPolicy"/>, sin permiso nuevo). El <c>ot_admin</c> trabaja SIEMPRE con el
/// organismo de su perfil y si pide otro recibe 403; el SuperAdmin indica <c>?transitOfficeId=</c>. Errores <c>{ code, message }</c>.
/// Un organismo sin dominio responde 404 <c>PORTAL_DOMINIO_NO_REGISTRADO</c> en el <c>GET</c>.
/// </summary>
public static class AdminOtPortalDominioEndpoints
{
    public static IEndpointRouteBuilder MapAdminOtPortalDominioEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/ot/portal-dominio")
            .RequireAuthorization(AdminAuthorization.OtAdminOrSuperAdminPolicy)
            .WithTags("Admin · OT · Portal Persona Natural · Dominio");

        group.MapGet(string.Empty, ObtenerAsync).WithName("AdminOtPortalDominioGet")
            .WithSummary("Dominio del portal del organismo: estado, motivo e instrucciones de DNS");
        group.MapPost(string.Empty, RegistrarAsync).WithName("AdminOtPortalDominioPost")
            .WithSummary("Registra el dominio del portal; queda pendiente con su token de verificación");
        group.MapPost("/verificar", VerificarAsync).WithName("AdminOtPortalDominioVerificar")
            .WithSummary("Comprueba ahora el TXT de DNS del dominio");
        group.MapDelete(string.Empty, RetirarAsync).WithName("AdminOtPortalDominioDelete")
            .WithSummary("Retira el dominio: el portal sigue en el enlace del punto");

        return app;
    }

    private static async Task<IResult> ObtenerAsync(
        HttpContext http, [FromQuery] Guid? transitOfficeId, [FromServices] IOtClientProcedureRepository bandeja,
        [FromServices] IPortalMarcaRepository organismos, [FromServices] PortalDominioAdminHandler handler, CancellationToken ct)
    {
        var (organismo, error) = await ResolverAsync(http.User, transitOfficeId, bandeja, organismos, ct).ConfigureAwait(false);
        return error ?? Resultado(await handler.ObtenerAsync(organismo, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> RegistrarAsync(
        HttpContext http, [FromBody] RegistrarPortalDominioRequest? body, [FromQuery] Guid? transitOfficeId,
        [FromServices] IOtClientProcedureRepository bandeja, [FromServices] IPortalMarcaRepository organismos,
        [FromServices] PortalDominioAdminHandler handler, CancellationToken ct)
    {
        var (organismo, error) = await ResolverAsync(http.User, transitOfficeId, bandeja, organismos, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (body is null)
            return Problema(400, PortalDominioErrores.Invalido, "Falta el dominio (campo host).");
        if (Actor(http.User) is not { } actor)
            return Problema(401, "TOKEN_INVALIDO", "Token inválido: falta el identificador del usuario.");

        return Resultado(await handler.RegistrarAsync(organismo, body, actor, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> VerificarAsync(
        HttpContext http, [FromQuery] Guid? transitOfficeId, [FromServices] IOtClientProcedureRepository bandeja,
        [FromServices] IPortalMarcaRepository organismos, [FromServices] PortalDominioAdminHandler handler, CancellationToken ct)
    {
        var (organismo, error) = await ResolverAsync(http.User, transitOfficeId, bandeja, organismos, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (Actor(http.User) is not { } actor)
            return Problema(401, "TOKEN_INVALIDO", "Token inválido: falta el identificador del usuario.");

        var resultado = await handler.VerificarAsync(organismo, actor, ct).ConfigureAwait(false);
        if (resultado.Error is { EsperarSegundos: { } espera })
            http.Response.Headers.RetryAfter = espera.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Resultado(resultado);
    }

    private static async Task<IResult> RetirarAsync(
        HttpContext http, [FromQuery] Guid? transitOfficeId, [FromServices] IOtClientProcedureRepository bandeja,
        [FromServices] IPortalMarcaRepository organismos, [FromServices] PortalDominioAdminHandler handler, CancellationToken ct)
    {
        var (organismo, error) = await ResolverAsync(http.User, transitOfficeId, bandeja, organismos, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (Actor(http.User) is not { } actor)
            return Problema(401, "TOKEN_INVALIDO", "Token inválido: falta el identificador del usuario.");

        var retirado = await handler.RetirarAsync(organismo, actor, ct).ConfigureAwait(false);
        return retirado.Error is { } e ? Problema(e.Status, e.Code, e.Message) : Results.NoContent();
    }

    // ── Alcance (mismo criterio que la marca del portal) ─────────────────────────────────────────────────────────────

    private static async Task<(Guid Organismo, IResult? Error)> ResolverAsync(
        ClaimsPrincipal user, Guid? transitOfficeId, IOtClientProcedureRepository bandeja, IPortalMarcaRepository organismos, CancellationToken ct)
    {
        if (EsSuperAdmin(user))
        {
            if (transitOfficeId is not { } elegido || elegido == Guid.Empty)
                return (Guid.Empty, Problema(400, PortalMarcaErrores.OrganismoRequerido, "Indique el organismo con el parámetro transitOfficeId."));
            if (!await organismos.ExisteOrganismoAsync(elegido, ct).ConfigureAwait(false))
                return (Guid.Empty, Problema(404, PortalMarcaErrores.OrganismoNoExiste, "El organismo indicado no existe."));
            return (elegido, null);
        }

        var propio = RequestTenantResolver.TryResolveTenantId(user, out var tenantId)
            ? await bandeja.ResolveTransitOfficeIdAsync(tenantId, null, ct).ConfigureAwait(false)
            : null;
        if (propio is null)
            return (Guid.Empty, Problema(403, PortalMarcaErrores.Prohibido, "Su usuario no tiene un organismo de tránsito asociado."));
        if (transitOfficeId is { } pedido && pedido != Guid.Empty && pedido != propio)
            return (Guid.Empty, Problema(403, PortalMarcaErrores.Prohibido, "Solo puede ver y cambiar el dominio del portal de su propio organismo."));
        return (propio.Value, null);
    }

    private static bool EsSuperAdmin(ClaimsPrincipal user) =>
        user.IsInRole(AdminAuthorization.SuperAdminRole)
        || string.Equals(user.FindFirstValue(AdminAuthorization.RoleClaimType), AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase);

    private static Guid? Actor(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static IResult Resultado(PortalDominioResultado<PortalDominioDto> r) =>
        r.Error is { } e ? Problema(e.Status, e.Code, e.Message) : Results.Ok(r.Valor);

    private static IResult Problema(int estado, string code, string message) =>
        Results.Json(new { code, message }, statusCode: estado);
}
