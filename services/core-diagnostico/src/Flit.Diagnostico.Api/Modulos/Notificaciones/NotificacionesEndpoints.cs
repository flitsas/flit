using System.Globalization;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Notificaciones;

/// <summary>
/// Bandeja de la campana (HU #14563 B8, AC36-AC38, ADR-0086 D-3), con <c>diagnostico.diagnosticos.ver</c>:
/// <list type="bullet">
/// <item><c>GET /notificaciones?limite=20&amp;antes={id}</c>: <c>{ no_leidas, items, siguiente }</c> con ETag débil
/// <c>W/"&lt;no_leidas&gt;-&lt;total&gt;-&lt;max creada_en&gt;-&lt;max leida_en&gt;"</c> (más el cursor y el límite pedidos); con
/// <c>If-None-Match</c> igual, 304 sin cuerpo.</item>
/// <item><c>POST /notificaciones/{nid}/leida</c>: 204, idempotente; <c>leida_en = GREATEST(ahora, creada_en)</c>; 404 si no es
/// del usuario o su lote ya no es visible.</item>
/// <item><c>POST /notificaciones/leer-todas</c>: 204.</item>
/// </list>
/// Solo las del usuario del token (<c>sub</c>), de su compañía (filtro de empresa) y de lotes que ve según <see cref="AccesoLote"/>
/// (visibilidad <c>PROPIOS</c>). Título y mensaje se arman al leer con rótulos fijos y el nombre del lote: sin placa, VIN,
/// documento, persona ni archivo.
/// </summary>
internal static class NotificacionesEndpoints
{
    public const string Ruta = "/notificaciones";

    public const string RutaLeida = Ruta + "/{nid:guid}/leida";

    public const string RutaLeerTodas = Ruta + "/leer-todas";

    private const int LimitePorDefecto = 20;

    private const int LimiteMaximo = 50;

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapGet(Ruta, BandejaAsync).RequirePermiso(Permisos.DiagnosticosVer).WithName("BandejaNotificaciones");
        api.MapPost(RutaLeida, LeidaAsync).RequirePermiso(Permisos.DiagnosticosVer).WithName("MarcarNotificacionLeida");
        api.MapPost(RutaLeerTodas, LeerTodasAsync).RequirePermiso(Permisos.DiagnosticosVer).WithName("LeerTodasNotificaciones");
    }

    /// <summary>Las notificaciones del usuario sobre lotes que puede ver (la misma regla para lista, no_leidas, ETag y marcado).</summary>
    internal static async Task<IQueryable<Notificacion>> VisiblesAsync(DiagnosticoDb db, UsuarioActual usuario, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(usuario);
        var lotes = await AccesoLote.VisiblesAsync(db.Set<CabeceraDiagnostico>(), db, usuario, ct).ConfigureAwait(false);
        var propio = usuario.Id;
        return db.Set<Notificacion>().Where(n => n.UsuarioId == propio && lotes.Any(c => c.Id == n.DiagnosticoId));
    }

    private static async Task<IResult> BandejaAsync(
        int? limite, Guid? antes, DiagnosticoDb db, UsuarioActual usuario, HttpRequest peticion, HttpResponse respuesta, CancellationToken ct)
    {
        var visibles = (await VisiblesAsync(db, usuario, ct).ConfigureAwait(false)).AsNoTracking();
        var noLeidas = await visibles.CountAsync(n => n.LeidaEn == null, ct).ConfigureAwait(false);
        var total = await visibles.CountAsync(ct).ConfigureAwait(false);
        var maxCreada = await visibles.MaxAsync(n => (DateTimeOffset?)n.CreadaEn, ct).ConfigureAwait(false);
        var maxLeida = await visibles.MaxAsync(n => n.LeidaEn, ct).ConfigureAwait(false);
        var tope = Math.Clamp(limite ?? LimitePorDefecto, 1, LimiteMaximo);
        var etag = string.Create(CultureInfo.InvariantCulture,
            $"W/\"{noLeidas}-{total}-{maxCreada?.ToUnixTimeMilliseconds() ?? 0}-{maxLeida?.ToUnixTimeMilliseconds() ?? 0}-{antes?.ToString("N") ?? "0"}-{tope}\"");
        respuesta.Headers.CacheControl = "private, no-cache";
        respuesta.Headers.ETag = etag;
        if (peticion.Headers.IfNoneMatch.Any(v => string.Equals(v, etag, StringComparison.Ordinal)))
            return Results.StatusCode(StatusCodes.Status304NotModified);

        var pagina = visibles;
        if (antes is { } cursor)
        {
            var ancla = await visibles.Where(n => n.Id == cursor).Select(n => (DateTimeOffset?)n.CreadaEn)
                .SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (ancla is { } creada)
                pagina = pagina.Where(n => n.CreadaEn < creada || (n.CreadaEn == creada && n.Id.CompareTo(cursor) < 0));
        }

        var filas = await pagina
            .Join(db.Set<CabeceraDiagnostico>(), n => n.DiagnosticoId, c => c.Id,
                (n, c) => new { n.Id, n.Tipo, n.CreadaEn, n.LeidaEn, n.DiagnosticoId, Lote = c.Nombre })
            .OrderByDescending(f => f.CreadaEn).ThenByDescending(f => f.Id).Take(tope + 1)
            .ToListAsync(ct).ConfigureAwait(false);
        var hay = filas.Count > tope;
        var items = filas.Take(tope).Select(f => new NotificacionDto(
            f.Id, f.Tipo, f.CreadaEn, f.LeidaEn, f.DiagnosticoId, Titulo(f.Tipo), Mensaje(f.Tipo, f.Lote),
            new AccionNotificacionDto(Accion(f.Tipo), $"/diagnostico/{f.DiagnosticoId}"))).ToList();
        return Results.Ok(new BandejaNotificacionesDto(noLeidas, items, hay ? items[^1].Id : null));
    }

    private static async Task<IResult> LeidaAsync(Guid nid, DiagnosticoDb db, UsuarioActual usuario, TimeProvider reloj, CancellationToken ct)
    {
        var propia = (await VisiblesAsync(db, usuario, ct).ConfigureAwait(false)).Where(n => n.Id == nid);
        if (!await propia.AnyAsync(ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("La notificación");
        var ahora = reloj.GetUtcNow();
        await propia.Where(n => n.LeidaEn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.LeidaEn, n => n.CreadaEn > ahora ? n.CreadaEn : ahora), ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> LeerTodasAsync(DiagnosticoDb db, UsuarioActual usuario, TimeProvider reloj, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        await (await VisiblesAsync(db, usuario, ct).ConfigureAwait(false)).Where(n => n.LeidaEn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.LeidaEn, n => n.CreadaEn > ahora ? n.CreadaEn : ahora), ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static string Titulo(string tipo) => tipo switch
    {
        Notificacion.ZipEvidenciasListo => "Evidencias listas",
        Notificacion.ZipEvidenciasFallido => "No se pudieron generar las evidencias",
        Notificacion.DerivacionTerminada => "Derivación terminada",
        Notificacion.LoteTerminado => "Diagnóstico terminado",
        _ => "Notificación",
    };

    private static string Mensaje(string tipo, string lote) => tipo switch
    {
        Notificacion.ZipEvidenciasListo => $"El ZIP de evidencias del lote «{lote}» está listo para descargar.",
        Notificacion.ZipEvidenciasFallido => $"El ZIP de evidencias del lote «{lote}» no se pudo generar. Puedes pedirlo de nuevo.",
        Notificacion.DerivacionTerminada => $"Terminó la derivación a trámite del lote «{lote}».",
        Notificacion.LoteTerminado => $"El lote «{lote}» terminó sus consultas.",
        _ => $"Lote «{lote}».",
    };

    private static string Accion(string tipo) => tipo switch
    {
        Notificacion.ZipEvidenciasListo or Notificacion.ZipEvidenciasFallido => "VER_EVIDENCIAS",
        Notificacion.DerivacionTerminada => "VER_DERIVACION",
        _ => "VER_LOTE",
    };
}

/// <summary>Respuesta de <c>GET /notificaciones</c>.</summary>
public sealed record BandejaNotificacionesDto(
    [property: JsonPropertyName("no_leidas")] int NoLeidas,
    [property: JsonPropertyName("items")] IReadOnlyList<NotificacionDto> Items,
    [property: JsonPropertyName("siguiente")] Guid? Siguiente);

/// <summary>Una notificación de la bandeja, sin PII.</summary>
public sealed record NotificacionDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("creada_en")] DateTimeOffset CreadaEn,
    [property: JsonPropertyName("leida_en")] DateTimeOffset? LeidaEn,
    [property: JsonPropertyName("diagnostico_id")] Guid DiagnosticoId,
    [property: JsonPropertyName("titulo")] string Titulo,
    [property: JsonPropertyName("mensaje")] string Mensaje,
    [property: JsonPropertyName("accion")] AccionNotificacionDto Accion);

/// <summary>A dónde lleva la notificación.</summary>
public sealed record AccionNotificacionDto(
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("href")] string Href);
