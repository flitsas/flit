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
        group.MapPost("/logo", (HttpContext h, IFormFile? file, [FromQuery] Guid? transitOfficeId, [FromServices] IOtClientProcedureRepository b,
                [FromServices] IPortalMarcaRepository r, [FromServices] PortalMarcaImagenHandler hd, CancellationToken ct) =>
                SubirAsync(h, file, transitOfficeId, PortalMarcaImagen.KindLogo, b, r, hd, ct))
            .WithName("AdminOtPortalMarcaLogoPost").DisableAntiforgery()
            .WithSummary("Carga el logo (PNG, JPEG o WebP, hasta 512 KB)");
        group.MapPost("/banner", (HttpContext h, IFormFile? file, [FromQuery] Guid? transitOfficeId, [FromServices] IOtClientProcedureRepository b,
                [FromServices] IPortalMarcaRepository r, [FromServices] PortalMarcaImagenHandler hd, CancellationToken ct) =>
                SubirAsync(h, file, transitOfficeId, PortalMarcaImagen.KindBanner, b, r, hd, ct))
            .WithName("AdminOtPortalMarcaBannerPost").DisableAntiforgery()
            .WithSummary("Carga el banner (PNG, JPEG o WebP, hasta 2 MB)");
        group.MapDelete("/logo", (HttpContext h, [FromQuery] Guid? transitOfficeId, [FromServices] IOtClientProcedureRepository b,
                [FromServices] IPortalMarcaRepository r, [FromServices] PortalMarcaImagenHandler hd, CancellationToken ct) =>
                QuitarAsync(h, transitOfficeId, PortalMarcaImagen.KindLogo, b, r, hd, ct))
            .WithName("AdminOtPortalMarcaLogoDelete").WithSummary("Quita el logo del borrador");
        group.MapDelete("/banner", (HttpContext h, [FromQuery] Guid? transitOfficeId, [FromServices] IOtClientProcedureRepository b,
                [FromServices] IPortalMarcaRepository r, [FromServices] PortalMarcaImagenHandler hd, CancellationToken ct) =>
                QuitarAsync(h, transitOfficeId, PortalMarcaImagen.KindBanner, b, r, hd, ct))
            .WithName("AdminOtPortalMarcaBannerDelete").WithSummary("Quita el banner del borrador");

        // Carrusel de imágenes de la columna lateral: hasta 6 imágenes del borrador, con texto alternativo y enlace opcional.
        group.MapPost("/carrusel", CarruselAgregarAsync).WithName("AdminOtPortalMarcaCarruselPost").DisableAntiforgery()
            .WithSummary("Agrega una imagen al final del carrusel (PNG, JPEG o WebP, hasta 512 KB; máximo 6)");
        group.MapPut("/carrusel/orden", CarruselOrdenarAsync).WithName("AdminOtPortalMarcaCarruselOrden")
            .WithSummary("Reordena el carrusel con una permutación exacta de las imágenes actuales");
        group.MapPut("/carrusel/{imageId:guid}", CarruselActualizarAsync).WithName("AdminOtPortalMarcaCarruselPut")
            .WithSummary("Cambia el texto alternativo y el enlace de una imagen del carrusel");
        group.MapDelete("/carrusel/{imageId:guid}", CarruselQuitarAsync).WithName("AdminOtPortalMarcaCarruselDelete")
            .WithSummary("Quita una imagen del carrusel del borrador");

        // Banners principales del portal: hasta 6 del borrador, con texto alternativo y enlace opcional. El banner único legacy
        // (POST/DELETE /banner) opera sobre el primero de esta lista.
        group.MapPost("/banners", BannersAgregarAsync).WithName("AdminOtPortalMarcaBannersPost").DisableAntiforgery()
            .WithSummary("Agrega un banner al final de la lista (PNG, JPEG o WebP, hasta 2 MB; máximo 6)");
        group.MapPut("/banners/orden", BannersOrdenarAsync).WithName("AdminOtPortalMarcaBannersOrden")
            .WithSummary("Reordena los banners con una permutación exacta de los actuales");
        group.MapPut("/banners/{imageId:guid}", BannersActualizarAsync).WithName("AdminOtPortalMarcaBannersPut")
            .WithSummary("Cambia el texto alternativo y el enlace de un banner");
        group.MapDelete("/banners/{imageId:guid}", BannersQuitarAsync).WithName("AdminOtPortalMarcaBannersDelete")
            .WithSummary("Quita un banner del borrador");
        group.MapGet("/imagenes/{id:guid}", LeerImagenAsync).WithName("AdminOtPortalMarcaImagen")
            .WithSummary("Imagen de la marca, también de borrador, solo con sesión y del organismo propio");

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

    private static async Task<IResult> SubirAsync(
        HttpContext http, IFormFile? file, Guid? transitOfficeId, string kind, IOtClientProcedureRepository bandeja,
        IPortalMarcaRepository repo, PortalMarcaImagenHandler handler, CancellationToken ct)
    {
        var (organismo, error) = await ResolverAsync(http.User, transitOfficeId, bandeja, repo, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (file is null)
            return Problema(400, PortalMarcaErrores.ArchivoInvalido, $"Falta el archivo del {PortalMarcaReglas.NombreImagen(kind)} (campo «file»).");
        if (Actor(http.User) is not { } actor)
            return Problema(401, "TOKEN_INVALIDO", "Token inválido: falta el identificador del usuario.");

        var stream = file.OpenReadStream();
        await using (stream.ConfigureAwait(false))
            return Resultado(await handler.SubirAsync(organismo, kind, file.FileName, stream, actor, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> QuitarAsync(
        HttpContext http, Guid? transitOfficeId, string kind, IOtClientProcedureRepository bandeja,
        IPortalMarcaRepository repo, PortalMarcaImagenHandler handler, CancellationToken ct)
    {
        var (organismo, error) = await ResolverAsync(http.User, transitOfficeId, bandeja, repo, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (Actor(http.User) is not { } actor)
            return Problema(401, "TOKEN_INVALIDO", "Token inválido: falta el identificador del usuario.");

        return Results.Ok(await handler.QuitarAsync(organismo, kind, actor, ct).ConfigureAwait(false));
    }

    // ── Carrusel ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> CarruselAgregarAsync(
        HttpContext http, IFormFile? file, [FromForm] string? alt, [FromForm] string? linkUrl, [FromQuery] Guid? transitOfficeId,
        [FromServices] IOtClientProcedureRepository bandeja, [FromServices] IPortalMarcaRepository repo,
        [FromServices] PortalMarcaImagenHandler handler, CancellationToken ct)
    {
        var (organismo, error) = await ResolverAsync(http.User, transitOfficeId, bandeja, repo, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (file is null)
            return Problema(400, PortalMarcaErrores.ArchivoInvalido, "Falta el archivo de la imagen del carrusel (campo «file»).");
        if (Actor(http.User) is not { } actor)
            return Problema(401, "TOKEN_INVALIDO", "Token inválido: falta el identificador del usuario.");

        var stream = file.OpenReadStream();
        await using (stream.ConfigureAwait(false))
            return Resultado(await handler.AgregarCarruselAsync(organismo, file.FileName, stream, alt, linkUrl, actor, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> CarruselActualizarAsync(
        HttpContext http, Guid imageId, [FromBody] ActualizarCarruselRequest? body, [FromQuery] Guid? transitOfficeId,
        [FromServices] IOtClientProcedureRepository bandeja, [FromServices] IPortalMarcaRepository repo,
        [FromServices] PortalMarcaImagenHandler handler, CancellationToken ct)
    {
        var (organismo, error) = await ResolverAsync(http.User, transitOfficeId, bandeja, repo, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (body is null)
            return Problema(400, PortalMarcaErrores.Datos, "Falta el cuerpo de la solicitud.");
        if (Actor(http.User) is not { } actor)
            return Problema(401, "TOKEN_INVALIDO", "Token inválido: falta el identificador del usuario.");

        return Resultado(await handler.ActualizarCarruselAsync(organismo, imageId, body.Alt, body.LinkUrl, actor, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> CarruselOrdenarAsync(
        HttpContext http, [FromBody] OrdenCarruselRequest? body, [FromQuery] Guid? transitOfficeId,
        [FromServices] IOtClientProcedureRepository bandeja, [FromServices] IPortalMarcaRepository repo,
        [FromServices] PortalMarcaImagenHandler handler, CancellationToken ct)
    {
        var (organismo, error) = await ResolverAsync(http.User, transitOfficeId, bandeja, repo, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (body?.ImageIds is not { } textos)
            return Problema(400, PortalMarcaErrores.Datos, "Falta la lista de imágenes (imageIds).");
        var ids = new List<Guid>(textos.Count);
        foreach (var t in textos)
        {
            if (!Guid.TryParse(t, out var id))
                return Problema(400, PortalMarcaErrores.Datos, "Cada elemento de imageIds debe ser un identificador de imagen válido.");
            ids.Add(id);
        }

        if (Actor(http.User) is not { } actor)
            return Problema(401, "TOKEN_INVALIDO", "Token inválido: falta el identificador del usuario.");

        return Resultado(await handler.OrdenarCarruselAsync(organismo, ids, actor, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> CarruselQuitarAsync(
        HttpContext http, Guid imageId, [FromQuery] Guid? transitOfficeId, [FromServices] IOtClientProcedureRepository bandeja,
        [FromServices] IPortalMarcaRepository repo, [FromServices] PortalMarcaImagenHandler handler, CancellationToken ct)
    {
        var (organismo, error) = await ResolverAsync(http.User, transitOfficeId, bandeja, repo, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (Actor(http.User) is not { } actor)
            return Problema(401, "TOKEN_INVALIDO", "Token inválido: falta el identificador del usuario.");

        return Resultado(await handler.QuitarCarruselAsync(organismo, imageId, actor, ct).ConfigureAwait(false));
    }

    // ── Banners ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> BannersAgregarAsync(
        HttpContext http, IFormFile? file, [FromForm] string? alt, [FromForm] string? linkUrl, [FromQuery] Guid? transitOfficeId,
        [FromServices] IOtClientProcedureRepository bandeja, [FromServices] IPortalMarcaRepository repo,
        [FromServices] PortalMarcaImagenHandler handler, CancellationToken ct)
    {
        var (organismo, error) = await ResolverAsync(http.User, transitOfficeId, bandeja, repo, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (file is null)
            return Problema(400, PortalMarcaErrores.ArchivoInvalido, "Falta el archivo del banner (campo «file»).");
        if (Actor(http.User) is not { } actor)
            return Problema(401, "TOKEN_INVALIDO", "Token inválido: falta el identificador del usuario.");

        var stream = file.OpenReadStream();
        await using (stream.ConfigureAwait(false))
            return Resultado(await handler.AgregarBannerAsync(organismo, file.FileName, stream, alt, linkUrl, actor, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> BannersActualizarAsync(
        HttpContext http, Guid imageId, [FromBody] ActualizarBannerRequest? body, [FromQuery] Guid? transitOfficeId,
        [FromServices] IOtClientProcedureRepository bandeja, [FromServices] IPortalMarcaRepository repo,
        [FromServices] PortalMarcaImagenHandler handler, CancellationToken ct)
    {
        var (organismo, error) = await ResolverAsync(http.User, transitOfficeId, bandeja, repo, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (body is null)
            return Problema(400, PortalMarcaErrores.Datos, "Falta el cuerpo de la solicitud.");
        if (Actor(http.User) is not { } actor)
            return Problema(401, "TOKEN_INVALIDO", "Token inválido: falta el identificador del usuario.");

        return Resultado(await handler.ActualizarBannerAsync(organismo, imageId, body.Alt, body.LinkUrl, actor, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> BannersOrdenarAsync(
        HttpContext http, [FromBody] OrdenBannersRequest? body, [FromQuery] Guid? transitOfficeId,
        [FromServices] IOtClientProcedureRepository bandeja, [FromServices] IPortalMarcaRepository repo,
        [FromServices] PortalMarcaImagenHandler handler, CancellationToken ct)
    {
        var (organismo, error) = await ResolverAsync(http.User, transitOfficeId, bandeja, repo, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (body?.ImageIds is not { } textos)
            return Problema(400, PortalMarcaErrores.Datos, "Falta la lista de banners (imageIds).");
        var ids = new List<Guid>(textos.Count);
        foreach (var t in textos)
        {
            if (!Guid.TryParse(t, out var id))
                return Problema(400, PortalMarcaErrores.Datos, "Cada elemento de imageIds debe ser un identificador de banner válido.");
            ids.Add(id);
        }

        if (Actor(http.User) is not { } actor)
            return Problema(401, "TOKEN_INVALIDO", "Token inválido: falta el identificador del usuario.");

        return Resultado(await handler.OrdenarBannersAsync(organismo, ids, actor, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> BannersQuitarAsync(
        HttpContext http, Guid imageId, [FromQuery] Guid? transitOfficeId, [FromServices] IOtClientProcedureRepository bandeja,
        [FromServices] IPortalMarcaRepository repo, [FromServices] PortalMarcaImagenHandler handler, CancellationToken ct)
    {
        var (organismo, error) = await ResolverAsync(http.User, transitOfficeId, bandeja, repo, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (Actor(http.User) is not { } actor)
            return Problema(401, "TOKEN_INVALIDO", "Token inválido: falta el identificador del usuario.");

        return Resultado(await handler.QuitarBannerAsync(organismo, imageId, actor, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> LeerImagenAsync(
        HttpContext http, Guid id, [FromServices] IOtClientProcedureRepository bandeja, [FromServices] PortalMarcaImagenHandler handler,
        CancellationToken ct)
    {
        if (await handler.BuscarAsync(id, ct).ConfigureAwait(false) is not { } imagen)
            return Problema(404, PortalMarcaErrores.NoEncontrada, "La imagen no existe.");

        // El SuperAdmin ve la de cualquier organismo; el ot_admin solo la del suyo (si no, 403 y no se revela nada más).
        if (!EsSuperAdmin(http.User))
        {
            var propio = await OrganismoPropioAsync(http.User, bandeja, ct).ConfigureAwait(false);
            if (propio is null || propio != imagen.TransitOfficeId)
                return Prohibido();
        }

        if (await handler.AbrirAsync(imagen, ct).ConfigureAwait(false) is not { } contenido)
            return Problema(404, PortalMarcaErrores.NoEncontrada, "La imagen no existe.");

        http.Response.Headers.CacheControl = "private, no-cache";
        http.Response.Headers.ETag = $"\"{contenido.Sha256}\"";
        return Results.Stream(contenido.Contenido, contenido.ContentType);
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
