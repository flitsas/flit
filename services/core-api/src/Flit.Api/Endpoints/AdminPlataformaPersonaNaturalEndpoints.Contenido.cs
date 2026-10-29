using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Contenido administrable del portal de Persona Natural (HU #14073): preguntas frecuentes, banners, enlaces y publicación.
/// Todo lo que se edita es el BORRADOR; el portal solo ve lo publicado (<c>POST /contenido/publicar</c>).
/// </summary>
public static partial class AdminPlataformaPersonaNaturalEndpoints
{
    public sealed record GuardarFaqRequest(string? Pregunta, string? Respuesta, bool? Activa);

    public sealed record ActivaRequest(bool? Activa);

    public sealed record ActivoRequest(bool? Activo);

    public sealed record OrdenRequest(IReadOnlyList<Guid>? Ids);

    public sealed record EnlaceRequest(string? Url);

    private static void MapContenido(RouteGroupBuilder group)
    {
        group.MapGet("/contenido/preguntas", async (ContenidoPnHandler h, CancellationToken ct) => Results.Ok(await h.ListarFaqsAsync(ct).ConfigureAwait(false)))
            .WithName("AdminPnFaqList").WithSummary("Preguntas frecuentes del borrador, en su orden")
            .Produces<IReadOnlyList<FaqPn>>(StatusCodes.Status200OK);
        group.MapPost("/contenido/preguntas", CrearFaqAsync)
            .WithName("AdminPnFaqCreate").WithSummary("Crea una pregunta frecuente (al final de la lista)")
            .Produces<FaqPn>(StatusCodes.Status201Created).Produces(StatusCodes.Status400BadRequest);
        group.MapPut("/contenido/preguntas/orden", ReordenarFaqsAsync)
            .WithName("AdminPnFaqReorder").WithSummary("Ordena las preguntas según la lista de ids")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status400BadRequest);
        group.MapPut("/contenido/preguntas/{id:guid}", ActualizarFaqAsync)
            .WithName("AdminPnFaqUpdate").WithSummary("Edita una pregunta frecuente")
            .Produces<FaqPn>(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound);
        group.MapPatch("/contenido/preguntas/{id:guid}/activa", EstablecerFaqActivaAsync)
            .WithName("AdminPnFaqSetActive").WithSummary("Activa o desactiva una pregunta (deja de publicarse)")
            .Produces<FaqPn>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        group.MapGet("/contenido/banners", async (ContenidoPnHandler h, CancellationToken ct) => Results.Ok(await h.ListarBannersAsync(ct).ConfigureAwait(false)))
            .WithName("AdminPnBannerList").WithSummary("Banners del borrador, en su orden")
            .Produces<IReadOnlyList<BannerPn>>(StatusCodes.Status200OK);
        group.MapPost("/contenido/banners", CrearBannerAsync)
            .WithName("AdminPnBannerCreate").WithSummary("Crea un banner (multipart: imagen JPG/PNG/WEBP de hasta 2 MB y texto alternativo)")
            .DisableAntiforgery()
            .Produces<BannerPn>(StatusCodes.Status201Created).Produces(StatusCodes.Status400BadRequest);
        group.MapPut("/contenido/banners/orden", ReordenarBannersAsync)
            .WithName("AdminPnBannerReorder").WithSummary("Ordena los banners según la lista de ids")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status400BadRequest);
        group.MapPut("/contenido/banners/{id:guid}", ActualizarBannerAsync)
            .WithName("AdminPnBannerUpdate").WithSummary("Edita un banner (multipart; la imagen es opcional y reemplaza la actual)")
            .DisableAntiforgery()
            .Produces<BannerPn>(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound);
        group.MapPatch("/contenido/banners/{id:guid}/activo", EstablecerBannerActivoAsync)
            .WithName("AdminPnBannerSetActive").WithSummary("Activa o desactiva un banner (deja de publicarse)")
            .Produces<BannerPn>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);
        group.MapGet("/contenido/banners/{id:guid}/imagen", ImagenBannerAsync)
            .WithName("AdminPnBannerImage").WithSummary("Imagen del banner del borrador (vista previa)")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        group.MapGet("/contenido/enlaces", async (ContenidoPnHandler h, CancellationToken ct) => Results.Ok(await h.ListarEnlacesAsync(ct).ConfigureAwait(false)))
            .WithName("AdminPnLinkList").WithSummary("Los cuatro enlaces del portal (url nula = sin documento oficial)")
            .Produces<IReadOnlyList<EnlacePn>>(StatusCodes.Status200OK);
        group.MapPut("/contenido/enlaces/{codigo}", EstablecerEnlaceAsync)
            .WithName("AdminPnLinkSet").WithSummary("Guarda un enlace (url vacía = sin documento oficial; sin texto legal inventado)")
            .Produces<EnlacePn>(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/contenido/borrador", async (ContenidoPnHandler h, CancellationToken ct) => Results.Ok(await h.BorradorAsync(ct).ConfigureAwait(false)))
            .WithName("AdminPnContentDraft").WithSummary("Vista previa: el contenido como lo vería el portal si se publicara hoy")
            .Produces<BorradorContenidoPn>(StatusCodes.Status200OK);
        group.MapPost("/contenido/publicar", PublicarAsync)
            .WithName("AdminPnContentPublish").WithSummary("Publica el borrador como versión nueva (fecha, usuario y versión anterior consultables)")
            .Produces<VersionContenidoPn>(StatusCodes.Status201Created).Produces(StatusCodes.Status409Conflict);
        group.MapGet("/contenido/versiones", async (ContenidoPnHandler h, CancellationToken ct) => Results.Ok(await h.ListarVersionesAsync(ct).ConfigureAwait(false)))
            .WithName("AdminPnContentVersions").WithSummary("Versiones publicadas, de la más reciente a la más antigua")
            .Produces<IReadOnlyList<ResumenVersionContenidoPn>>(StatusCodes.Status200OK);
        group.MapGet("/contenido/versiones/{version:int}", ObtenerVersionAsync)
            .WithName("AdminPnContentVersion").WithSummary("Una versión publicada (solo lectura)")
            .Produces<VersionContenidoPn>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);
    }

    // ── Preguntas frecuentes ──────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> CrearFaqAsync(GuardarFaqRequest body, ContenidoPnHandler h, HttpContext http, CancellationToken ct)
    {
        if (Actor(http) is not { } actor)
            return SinUsuario();
        var r = await h.CrearFaqAsync(body.Pregunta, body.Respuesta, body.Activa, actor, ct).ConfigureAwait(false);
        return r.Error is null ? Results.Created($"{BasePath}/contenido/preguntas/{r.Valor!.Id}", r.Valor) : ErrorContenido(r.Error, r.Mensaje!);
    }

    private static async Task<IResult> ActualizarFaqAsync(Guid id, GuardarFaqRequest body, ContenidoPnHandler h, HttpContext http, CancellationToken ct)
    {
        if (Actor(http) is not { } actor)
            return SinUsuario();
        var r = await h.ActualizarFaqAsync(id, body.Pregunta, body.Respuesta, body.Activa, actor, ct).ConfigureAwait(false);
        return r.Error is null ? Results.Ok(r.Valor) : ErrorContenido(r.Error, r.Mensaje!);
    }

    private static async Task<IResult> EstablecerFaqActivaAsync(Guid id, ActivaRequest body, ContenidoPnHandler h, HttpContext http, CancellationToken ct)
    {
        if (Actor(http) is not { } actor)
            return SinUsuario();
        if (body.Activa is not { } activa)
            return Error(StatusCodes.Status400BadRequest, "activa_obligatorio", "Indique si la pregunta queda activa o no.");
        var r = await h.EstablecerFaqActivaAsync(id, activa, actor, ct).ConfigureAwait(false);
        return r.Error is null ? Results.Ok(r.Valor) : ErrorContenido(r.Error, r.Mensaje!);
    }

    private static async Task<IResult> ReordenarFaqsAsync(OrdenRequest body, ContenidoPnHandler h, HttpContext http, CancellationToken ct)
    {
        if (Actor(http) is not { } actor)
            return SinUsuario();
        var r = await h.ReordenarFaqsAsync(body.Ids, actor, ct).ConfigureAwait(false);
        return r.Error is null ? Results.NoContent() : ErrorContenido(r.Error, r.Mensaje!);
    }

    // ── Banners ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> CrearBannerAsync(
        [FromForm] string? titulo, [FromForm] string? textoAlternativo, [FromForm] string? enlaceUrl, [FromForm] int? orden,
        [FromForm] DateTimeOffset? vigenteDesde, [FromForm] DateTimeOffset? vigenteHasta, [FromForm] bool? activo,
        IFormFile? file, ContenidoPnHandler h, HttpContext http, CancellationToken ct)
    {
        if (Actor(http) is not { } actor)
            return SinUsuario();
        await using var stream = file is { Length: > 0 } ? file.OpenReadStream() : null;
        var imagen = stream is null ? null : new ImagenSubidaPn(file!.FileName, file.ContentType, file.Length, stream);
        var r = await h.CrearBannerAsync(new DatosBannerPn(titulo, textoAlternativo, enlaceUrl, orden, vigenteDesde, vigenteHasta, activo, imagen), actor, ct).ConfigureAwait(false);
        return r.Error is null ? Results.Created($"{BasePath}/contenido/banners/{r.Valor!.Id}", r.Valor) : ErrorContenido(r.Error, r.Mensaje!);
    }

    private static async Task<IResult> ActualizarBannerAsync(
        Guid id, [FromForm] string? titulo, [FromForm] string? textoAlternativo, [FromForm] string? enlaceUrl, [FromForm] int? orden,
        [FromForm] DateTimeOffset? vigenteDesde, [FromForm] DateTimeOffset? vigenteHasta, [FromForm] bool? activo,
        IFormFile? file, ContenidoPnHandler h, HttpContext http, CancellationToken ct)
    {
        if (Actor(http) is not { } actor)
            return SinUsuario();
        await using var stream = file is { Length: > 0 } ? file.OpenReadStream() : null;
        var imagen = stream is null ? null : new ImagenSubidaPn(file!.FileName, file.ContentType, file.Length, stream);
        var r = await h.ActualizarBannerAsync(id, new DatosBannerPn(titulo, textoAlternativo, enlaceUrl, orden, vigenteDesde, vigenteHasta, activo, imagen), actor, ct).ConfigureAwait(false);
        return r.Error is null ? Results.Ok(r.Valor) : ErrorContenido(r.Error, r.Mensaje!);
    }

    private static async Task<IResult> EstablecerBannerActivoAsync(Guid id, ActivoRequest body, ContenidoPnHandler h, HttpContext http, CancellationToken ct)
    {
        if (Actor(http) is not { } actor)
            return SinUsuario();
        if (body.Activo is not { } activo)
            return Error(StatusCodes.Status400BadRequest, "activo_obligatorio", "Indique si el banner queda activo o no.");
        var r = await h.EstablecerBannerActivoAsync(id, activo, actor, ct).ConfigureAwait(false);
        return r.Error is null ? Results.Ok(r.Valor) : ErrorContenido(r.Error, r.Mensaje!);
    }

    private static async Task<IResult> ReordenarBannersAsync(OrdenRequest body, ContenidoPnHandler h, HttpContext http, CancellationToken ct)
    {
        if (Actor(http) is not { } actor)
            return SinUsuario();
        var r = await h.ReordenarBannersAsync(body.Ids, actor, ct).ConfigureAwait(false);
        return r.Error is null ? Results.NoContent() : ErrorContenido(r.Error, r.Mensaje!);
    }

    private static async Task<IResult> ImagenBannerAsync(Guid id, ContenidoPnHandler h, HttpContext http, CancellationToken ct)
    {
        if (await h.AbrirImagenAsync(id, ct).ConfigureAwait(false) is not { } imagen)
            return Error(StatusCodes.Status404NotFound, ContenidoPnErrores.NoEncontrado, "El banner no existe.");
        http.Response.Headers.CacheControl = "private, no-cache";
        http.Response.Headers.ETag = $"\"{imagen.Sha256}\"";
        return Results.Stream(imagen.Contenido, imagen.ContentType);
    }

    // ── Enlaces, borrador y publicación ───────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> EstablecerEnlaceAsync(string codigo, EnlaceRequest body, ContenidoPnHandler h, HttpContext http, CancellationToken ct)
    {
        if (Actor(http) is not { } actor)
            return SinUsuario();
        var r = await h.EstablecerEnlaceAsync(codigo, body.Url, actor, ct).ConfigureAwait(false);
        return r.Error is null ? Results.Ok(r.Valor) : ErrorContenido(r.Error, r.Mensaje!);
    }

    private static async Task<IResult> PublicarAsync(ContenidoPnHandler h, HttpContext http, CancellationToken ct)
    {
        if (Actor(http) is not { } actor)
            return SinUsuario();
        var r = await h.PublicarAsync(actor, ct).ConfigureAwait(false);
        return r.Error is null ? Results.Created($"{BasePath}/contenido/versiones/{r.Valor!.Version}", r.Valor) : ErrorContenido(r.Error, r.Mensaje!);
    }

    private static async Task<IResult> ObtenerVersionAsync(int version, ContenidoPnHandler h, CancellationToken ct) =>
        await h.ObtenerVersionAsync(version, ct).ConfigureAwait(false) is { } v
            ? Results.Ok(v)
            : Error(StatusCodes.Status404NotFound, ContenidoPnErrores.NoEncontrado, "La versión no existe.");

    // ── Soporte ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Guid? Actor(HttpContext http) => AdminCompaniesDomainEndpoints.ResolveUserId(http.User);

    private static IResult SinUsuario() => Error(StatusCodes.Status401Unauthorized, "usuario_no_identificado", "No se pudo identificar al usuario.");

    private static IResult ErrorContenido(string error, string mensaje) => error switch
    {
        ContenidoPnErrores.NoEncontrado => Error(StatusCodes.Status404NotFound, error, mensaje),
        ContenidoPnErrores.SinCambios => Error(StatusCodes.Status409Conflict, error, mensaje),
        _ => Error(StatusCodes.Status400BadRequest, error, mensaje),
    };
}
