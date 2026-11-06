using System.Security.Claims;
using Flit.Admin.Application.Companies.TransitOffices.PortalMarca;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Marca del portal Persona Natural de cada organismo (Feature #14374, Épica #14297). Rol <c>ot_admin</c> o SuperAdmin
/// (<see cref="AdminAuthorization.OtAdminOrSuperAdminPolicy"/>, sin permiso nuevo). El <c>ot_admin</c> trabaja SIEMPRE con el
/// organismo de su perfil y si pide otro recibe 403; el SuperAdmin indica <c>?transitOfficeId=</c>. Errores <c>{ code, message }</c>.
/// </summary>
public static class AdminOtPortalMarcaEndpoints
{
    public static IEndpointRouteBuilder MapAdminOtPortalMarcaEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/ot/portal-marca")
            .RequireAuthorization(AdminAuthorization.OtAdminOrSuperAdminPolicy)
            .WithTags("Admin · OT · Portal Persona Natural · Marca");

        group.MapGet(string.Empty, ObtenerAsync).WithName("AdminOtPortalMarcaGet")
            .WithSummary("Marca del portal del organismo: borrador, publicado y versión");
        group.MapPut(string.Empty, GuardarAsync).WithName("AdminOtPortalMarcaPut")
            .WithSummary("Guarda el borrador (nombre y colores) con control de versión");
        group.MapPost("/publish", PublicarAsync).WithName("AdminOtPortalMarcaPublish")
            .WithSummary("Publica el borrador: exige nombre, colores y contraste 4,5:1");
        group.MapPost("/retire", RetirarAsync).WithName("AdminOtPortalMarcaRetire")
            .WithSummary("Retira la marca publicada y conserva el borrador");

        return app;
    }

    private static async Task<IResult> ObtenerAsync(
        HttpContext http, [FromQuery] Guid? transitOfficeId, [FromServices] IOtClientProcedureRepository bandeja,
        [FromServices] IPortalMarcaRepository repo, [FromServices] PortalMarcaAdminHandler handler, CancellationToken ct)
    {
        var (organismo, error) = await ResolverAsync(http.User, transitOfficeId, bandeja, repo, ct).ConfigureAwait(false);
        return error ?? Results.Ok(await handler.ObtenerAsync(organismo, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> GuardarAsync(
        HttpContext http, [FromBody] GuardarPortalMarcaRequest? body, [FromQuery] Guid? transitOfficeId,
        [FromServices] IOtClientProcedureRepository bandeja, [FromServices] IPortalMarcaRepository repo,
        [FromServices] PortalMarcaAdminHandler handler, CancellationToken ct)
    {
        var (organismo, error) = await ResolverAsync(http.User, transitOfficeId, bandeja, repo, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (body is null)
            return Problema(400, PortalMarcaErrores.Datos, "Falta el cuerpo de la solicitud.");
        if (Actor(http.User) is not { } actor)
            return Problema(401, "TOKEN_INVALIDO", "Token inválido: falta el identificador del usuario.");

        return Resultado(await handler.GuardarAsync(organismo, body, actor, ct).ConfigureAwait(false));
    }

    private static Task<IResult> PublicarAsync(
        HttpContext http, [FromBody] VersionPortalMarcaRequest? body, [FromQuery] Guid? transitOfficeId,
        [FromServices] IOtClientProcedureRepository bandeja, [FromServices] IPortalMarcaRepository repo,
        [FromServices] PortalMarcaAdminHandler handler, CancellationToken ct) =>
        CambiarVersionAsync(http, body, transitOfficeId, bandeja, repo, (o, v, a) => handler.PublicarAsync(o, v, a, ct), ct);

    private static Task<IResult> RetirarAsync(
        HttpContext http, [FromBody] VersionPortalMarcaRequest? body, [FromQuery] Guid? transitOfficeId,
        [FromServices] IOtClientProcedureRepository bandeja, [FromServices] IPortalMarcaRepository repo,
        [FromServices] PortalMarcaAdminHandler handler, CancellationToken ct) =>
        CambiarVersionAsync(http, body, transitOfficeId, bandeja, repo, (o, v, a) => handler.RetirarAsync(o, v, a, ct), ct);

    private static async Task<IResult> CambiarVersionAsync(
        HttpContext http, VersionPortalMarcaRequest? body, Guid? transitOfficeId, IOtClientProcedureRepository bandeja,
        IPortalMarcaRepository repo, Func<Guid, long, Guid, Task<PortalMarcaResultado<PortalMarcaDto>>> accion, CancellationToken ct)
    {
        var (organismo, error) = await ResolverAsync(http.User, transitOfficeId, bandeja, repo, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (body is null)
            return Problema(400, PortalMarcaErrores.Datos, "Falta la versión (rowVersion) que leyó el editor.");
        if (Actor(http.User) is not { } actor)
            return Problema(401, "TOKEN_INVALIDO", "Token inválido: falta el identificador del usuario.");

        return Resultado(await accion(organismo, body.RowVersion, actor).ConfigureAwait(false));
    }

    // ── Alcance ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<(Guid Organismo, IResult? Error)> ResolverAsync(
        ClaimsPrincipal user, Guid? transitOfficeId, IOtClientProcedureRepository bandeja, IPortalMarcaRepository repo, CancellationToken ct)
    {
        if (EsSuperAdmin(user))
        {
            if (transitOfficeId is not { } elegido || elegido == Guid.Empty)
                return (Guid.Empty, Problema(400, PortalMarcaErrores.OrganismoRequerido, "Indique el organismo con el parámetro transitOfficeId."));
            if (!await repo.ExisteOrganismoAsync(elegido, ct).ConfigureAwait(false))
                return (Guid.Empty, Problema(404, PortalMarcaErrores.OrganismoNoExiste, "El organismo indicado no existe."));
            return (elegido, null);
        }

        var propio = await OrganismoPropioAsync(user, bandeja, ct).ConfigureAwait(false);
        if (propio is null)
            return (Guid.Empty, Problema(403, PortalMarcaErrores.Prohibido, "Su usuario no tiene un organismo de tránsito asociado."));
        if (transitOfficeId is { } pedido && pedido != Guid.Empty && pedido != propio)
            return (Guid.Empty, Prohibido());
        return (propio.Value, null);
    }

    private static async Task<Guid?> OrganismoPropioAsync(ClaimsPrincipal user, IOtClientProcedureRepository bandeja, CancellationToken ct) =>
        RequestTenantResolver.TryResolveTenantId(user, out var tenantId)
            ? await bandeja.ResolveTransitOfficeIdAsync(tenantId, null, ct).ConfigureAwait(false)
            : null;

    private static bool EsSuperAdmin(ClaimsPrincipal user) =>
        user.IsInRole(AdminAuthorization.SuperAdminRole)
        || string.Equals(user.FindFirstValue(AdminAuthorization.RoleClaimType), AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase);

    private static Guid? Actor(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static IResult Prohibido() =>
        Problema(403, PortalMarcaErrores.Prohibido, "Solo puede ver y cambiar la marca del portal de su propio organismo.");

    private static IResult Resultado(PortalMarcaResultado<PortalMarcaDto> r) =>
        r.Error is { } e ? Problema(e.Status, e.Code, e.Message) : Results.Ok(r.Valor);

    private static IResult Problema(int estado, string code, string message) =>
        Results.Json(new { code, message }, statusCode: estado);
}
