using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Grpc.V1;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.PersonaNatural.Api.Punto;

/// <summary>Marca del organismo tal como la ve el portal (contrato de la Épica #14297): sin datos internos ni borradores.</summary>
public sealed record MarcaPuntoRespuesta(
    string Origen, string? Nombre, ColoresMarcaPunto? Colores, string? LogoUrl, string? BannerUrl, int Version)
{
    public const string OrigenFlit = "flit";
    public const string OrigenOrganismo = "organismo";
    public const string OrigenWeb = "web";

    /// <summary>Marca FLIT: lo que recibe el portal ante un código inválido, un organismo sin marca o una falla.</summary>
    public static readonly MarcaPuntoRespuesta Flit = new(OrigenFlit, null, null, null, null, 0);
}

public sealed record ColoresMarcaPunto(string Primario, string Secundario, string SobrePrimario);

/// <summary>Imagen publicada con su ETag (sha256). <c>Contenido</c> nulo con <c>NoModificada</c> = el cliente ya la tiene.</summary>
public sealed record ImagenMarcaPunto(bool NoModificada, string Sha256, string ContentType, byte[]? Contenido);

/// <summary>Lectura de la marca contra la fachada de core-api. El BFF no lee las tablas y no guarda nada en caché.</summary>
public interface IMarcaPunto
{
    Task<MarcaPuntoRespuesta> ObtenerAsync(string codigo, CancellationToken ct);

    /// <summary>Marca publicada de la web de persona natural (HU #14401).</summary>
    Task<MarcaPuntoRespuesta> ObtenerWebAsync(CancellationToken ct);

    Task<ImagenMarcaPunto?> ObtenerImagenAsync(string imagenId, string? ifNoneMatch, CancellationToken ct);
}

internal sealed class MarcaPuntoNoConfigurada : IMarcaPunto
{
    public Task<MarcaPuntoRespuesta> ObtenerAsync(string codigo, CancellationToken ct) => Task.FromResult(MarcaPuntoRespuesta.Flit);

    public Task<MarcaPuntoRespuesta> ObtenerWebAsync(CancellationToken ct) => Task.FromResult(MarcaPuntoRespuesta.Flit);

    public Task<ImagenMarcaPunto?> ObtenerImagenAsync(string imagenId, string? ifNoneMatch, CancellationToken ct) =>
        Task.FromResult<ImagenMarcaPunto?>(null);
}

internal sealed class MarcaPuntoGrpc(PersonaNaturalService.PersonaNaturalServiceClient cliente) : IMarcaPunto
{
    private static Metadata Empresa() =>
        new() { { PlatformServiceCallInterceptor.TenantMetadata, PlatformTenants.PersonaNatural.ToString() } };

    public async Task<MarcaPuntoRespuesta> ObtenerAsync(string codigo, CancellationToken ct)
    {
        var r = await FachadaTramitesGrpc.Llamar(() => cliente.ObtenerMarcaPuntoAsync(
            new ObtenerMarcaPuntoRequest { Codigo = codigo }, Empresa(), cancellationToken: ct).ResponseAsync).ConfigureAwait(false);
        return ARespuesta(r, MarcaPuntoRespuesta.OrigenOrganismo);
    }

    public async Task<MarcaPuntoRespuesta> ObtenerWebAsync(CancellationToken ct)
    {
        var r = await FachadaTramitesGrpc.Llamar(() => cliente.ObtenerMarcaWebAsync(
            new ObtenerMarcaWebRequest(), Empresa(), cancellationToken: ct).ResponseAsync).ConfigureAwait(false);
        return ARespuesta(r, MarcaPuntoRespuesta.OrigenWeb);
    }

    /// <summary>Solo una marca completa y del origen esperado llega al portal; cualquier otra cosa es la marca FLIT.</summary>
    private static MarcaPuntoRespuesta ARespuesta(ObtenerMarcaPuntoResponse r, string origenEsperado)
    {
        if (!string.Equals(r.Origen, origenEsperado, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(r.Nombre)
            || string.IsNullOrWhiteSpace(r.ColorPrimario) || string.IsNullOrWhiteSpace(r.ColorSecundario) || string.IsNullOrWhiteSpace(r.ColorSobrePrimario))
            return MarcaPuntoRespuesta.Flit;

        return new MarcaPuntoRespuesta(
            origenEsperado,
            r.Nombre,
            new ColoresMarcaPunto(r.ColorPrimario, r.ColorSecundario, r.ColorSobrePrimario),
            UrlImagen(r.LogoId),
            UrlImagen(r.BannerId),
            r.Version);
    }

    public async Task<ImagenMarcaPunto?> ObtenerImagenAsync(string imagenId, string? ifNoneMatch, CancellationToken ct)
    {
        var r = await FachadaTramitesGrpc.Llamar(() => cliente.ObtenerImagenMarcaPuntoAsync(
            new ObtenerImagenMarcaPuntoRequest { ImagenId = imagenId, IfNoneMatch = ifNoneMatch ?? string.Empty }, Empresa(), cancellationToken: ct).ResponseAsync).ConfigureAwait(false);
        return r.Encontrado ? new ImagenMarcaPunto(r.NoModificado, r.Sha256, r.ContentType, r.NoModificado ? null : r.Contenido.ToByteArray()) : null;
    }

    private static string? UrlImagen(string id) =>
        Guid.TryParse(id, out var g) ? $"{MarcaPuntoEndpoints.RutaImagenes}{g}" : null;
}

/// <summary>
/// HU #14378 — marca publicada del organismo para el portal del punto.
/// <c>GET /api/v1/persona-natural/punto/marca/{codigo}</c> siempre responde 200: sin marca, con código inválido o ante una falla
/// entrega <c>origen: "flit"</c>. Sin caché por tiempo (<c>Cache-Control: no-store</c>): lo recién publicado o retirado se ve en
/// la siguiente lectura. <c>GET /api/v1/persona-natural/marca/imagenes/{id}</c> sirve solo imágenes publicadas, con URL
/// inmutable (el id cambia con cada versión), nunca el borrador.
/// </summary>
public static class MarcaPuntoEndpoints
{
    public const string RutaImagenes = SesionEndpoints.Base + "/marca/imagenes/";

    private const int CodigoMaxLargo = 20;
    private const int MaxIfNoneMatch = 100;

    public static IServiceCollection AddMarcaPunto(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IMarcaPunto>(sp =>
            sp.GetService<PersonaNaturalService.PersonaNaturalServiceClient>() is { } cliente
                ? new MarcaPuntoGrpc(cliente)
                : new MarcaPuntoNoConfigurada());
        return services;
    }

    public static IEndpointRouteBuilder MapMarcaPuntoEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var grupo = app.MapGroup(SesionEndpoints.Base).AllowAnonymous().RequireRateLimiting(AbusoEndpoints.PoliticaPorIp);

        grupo.MapGet("/punto/marca/{codigo}", async (string codigo, IMarcaPunto marca, HttpContext http, ILogger<IMarcaPunto> log, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var buscado = (codigo ?? string.Empty).Trim();
            if (buscado.Length is 0 or > CodigoMaxLargo)
                return Results.Ok(MarcaPuntoRespuesta.Flit);
            try
            {
                return Results.Ok(await marca.ObtenerAsync(buscado, ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
#pragma warning disable CA1848 // Mensaje fijo, solo en falla.
                log.LogWarning(ex, "No se pudo leer la marca del punto; se entrega la marca FLIT.");
#pragma warning restore CA1848
                return Results.Ok(MarcaPuntoRespuesta.Flit);
            }
        });

        // HU #14401 — marca de la web de persona natural: misma forma y mismas garantías que la del punto.
        grupo.MapGet("/marca/web", async (IMarcaPunto marca, HttpContext http, ILogger<IMarcaPunto> log, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            try
            {
                return Results.Ok(await marca.ObtenerWebAsync(ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
#pragma warning disable CA1848 // Mensaje fijo, solo en falla.
                log.LogWarning(ex, "No se pudo leer la marca de la web; se entrega la marca FLIT.");
#pragma warning restore CA1848
                return Results.Ok(MarcaPuntoRespuesta.Flit);
            }
        });

        grupo.MapGet("/marca/imagenes/{id}", async (string id, IMarcaPunto marca, HttpContext http, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var imagen))
                return SesionEndpoints.Problema(StatusCodes.Status404NotFound, "No encontrado", "La imagen no existe.", "no_encontrado");

            var etag = http.Request.Headers.IfNoneMatch.ToString().Trim().Trim('"');
            try
            {
                var r = await marca.ObtenerImagenAsync(imagen.ToString(), etag.Length is > 0 and <= MaxIfNoneMatch ? etag : null, ct).ConfigureAwait(false);
                if (r is null)
                    return SesionEndpoints.Problema(StatusCodes.Status404NotFound, "No encontrado", "La imagen no existe.", "no_encontrado");

                // El id cambia con cada versión de la imagen publicada: la URL es inmutable y puede cachearse un año.
                http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                http.Response.Headers.ETag = $"\"{r.Sha256}\"";
                return r.NoModificada || r.Contenido is null
                    ? Results.StatusCode(StatusCodes.Status304NotModified)
                    : Results.Bytes(r.Contenido, r.ContentType);
            }
            catch (FachadaException ex)
            {
                return SesionEndpoints.Problema(ex.EstadoHttp, "No disponible", ex.Message, ex.Codigo);
            }
        });

        return app;
    }
}
