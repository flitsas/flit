using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.Tramites.Grpc.V1;

namespace Flit.PersonaNatural.Api.Configuracion;

// ── Lo que sale al portal (HU #14073). Texto plano; sin datos bancarios ni parámetros de protección. ─────────────────────

public sealed record PreguntaDto(string Id, string Pregunta, string Respuesta, int Orden);

/// <param name="ImagenUrl">Ruta relativa del BFF que sirve la imagen; <c>?v=</c> lleva la versión para que la caché del navegador se renueve al cambiarla.</param>
public sealed record BannerDto(string Id, string Titulo, string TextoAlternativo, string? EnlaceUrl, int Orden, string ImagenUrl);

/// <param name="ConDocumento">Falso = sin documento oficial: el portal conserva su aviso actual y no inventa contenido legal.</param>
public sealed record EnlaceDto(string Codigo, string? Url, bool ConDocumento);

public sealed record SoporteDto(string? Correo, string? Telefono, string? Horario);

/// <param name="Version">Versión publicada del contenido; <c>"0"</c> = nada publicado (el portal conserva su contenido actual).</param>
public sealed record ConfiguracionPortalDto(
    string Version,
    DateTimeOffset? PublicadoEn,
    IReadOnlyList<PreguntaDto> Preguntas,
    IReadOnlyList<BannerDto> Banners,
    IReadOnlyList<EnlaceDto> Enlaces,
    SoporteDto Soporte,
    int? BorradorVigenciaDias);

// ── Lo que entrega la fachada de core-api al BFF. ────────────────────────────────────────────────────────────────────────

/// <summary>Un banner tal como lo entrega core-api: lleva la versión (sha256) de su imagen, que el BFF convierte en <c>?v=</c>.</summary>
public sealed record BannerFachada(string Id, string Titulo, string TextoAlternativo, string? EnlaceUrl, int Orden, string ImagenSha256);

public sealed record ConfiguracionFachada(
    string Version,
    DateTimeOffset? PublicadoEn,
    IReadOnlyList<PreguntaDto> Preguntas,
    IReadOnlyList<BannerFachada> Banners,
    IReadOnlyList<EnlaceDto> Enlaces,
    SoporteDto Soporte,
    int? BorradorVigenciaDias);

/// <summary>Imagen de un banner publicado: <see cref="NoModificado"/> cuando el cliente ya tiene esa versión (sin bytes).</summary>
public sealed record ImagenBannerFachada(bool NoModificado, string Sha256, string ContentType, byte[] Contenido);

/// <summary>
/// Lo que el BFF le pide a la fachada de core-api para el contenido del portal (HU #14073). Interfaz hermana de
/// <see cref="IFachadaTramites"/>: su propio contrato y su propia fachada falsa en las pruebas.
/// </summary>
public interface IFachadaConfiguracion
{
    Task<ConfiguracionFachada> ObtenerAsync(CancellationToken ct);

    /// <summary>La imagen del banner publicado y vigente; <c>null</c> si no existe, no está publicado o venció.</summary>
    Task<ImagenBannerFachada?> ObtenerImagenAsync(string bannerId, string? ifNoneMatch, CancellationToken ct);
}

internal sealed class FachadaConfiguracionNoConfigurada : IFachadaConfiguracion
{
    private static FachadaException NoDisponible() =>
        new("servicio_no_disponible", "El contenido del portal no está disponible por ahora.", StatusCodes.Status503ServiceUnavailable);

    public Task<ConfiguracionFachada> ObtenerAsync(CancellationToken ct) => throw NoDisponible();

    public Task<ImagenBannerFachada?> ObtenerImagenAsync(string bannerId, string? ifNoneMatch, CancellationToken ct) => throw NoDisponible();
}

/// <summary>
/// Cliente de los RPC <c>ObtenerConfiguracionPortal</c> y <c>ObtenerImagenBannerPortal</c> con el token de servicio del SDK y la
/// empresa de Persona Natural. Reutiliza el cliente gRPC que registra <see cref="CotizacionesEndpoints.AddCotizacionesPortal"/>.
/// </summary>
internal sealed class FachadaConfiguracionGrpc(PersonaNaturalService.PersonaNaturalServiceClient cliente) : IFachadaConfiguracion
{
    private static global::Grpc.Core.Metadata Empresa() =>
        new() { { Flit.Platform.Sdk.Grpc.PlatformServiceCallInterceptor.TenantMetadata, Flit.Platform.Sdk.PlatformTenants.PersonaNatural.ToString() } };

    public async Task<ConfiguracionFachada> ObtenerAsync(CancellationToken ct)
    {
        var r = await FachadaTramitesGrpc.Llamar(() => cliente.ObtenerConfiguracionPortalAsync(
            new ObtenerConfiguracionPortalRequest(), Empresa(), cancellationToken: ct).ResponseAsync).ConfigureAwait(false);
        return new ConfiguracionFachada(
            r.Version,
            r.PublicadoEn is null ? null : r.PublicadoEn.ToDateTimeOffset(),
            [.. r.Preguntas.Select(p => new PreguntaDto(p.Id, p.Pregunta, p.Respuesta, p.Orden))],
            [.. r.Banners.Select(b => new BannerFachada(b.Id, b.Titulo, b.TextoAlternativo, Vacio(b.EnlaceUrl), b.Orden, b.ImagenSha256))],
            [.. r.Enlaces.Select(e => new EnlaceDto(e.Codigo, Vacio(e.Url), e.ConDocumento))],
            new SoporteDto(Vacio(r.Soporte?.Correo), Vacio(r.Soporte?.Telefono), Vacio(r.Soporte?.Horario)),
            r.Parametros is { BorradorVigenciaDias: > 0 } p ? p.BorradorVigenciaDias : null);
    }

    public async Task<ImagenBannerFachada?> ObtenerImagenAsync(string bannerId, string? ifNoneMatch, CancellationToken ct)
    {
        var r = await FachadaTramitesGrpc.Llamar(() => cliente.ObtenerImagenBannerPortalAsync(
            new ObtenerImagenBannerPortalRequest { BannerId = bannerId, IfNoneMatch = ifNoneMatch ?? string.Empty }, Empresa(), cancellationToken: ct).ResponseAsync).ConfigureAwait(false);
        return r.Encontrado ? new ImagenBannerFachada(r.NoModificado, r.Sha256, r.ContentType, r.Contenido.ToByteArray()) : null;
    }

    private static string? Vacio(string? valor) => string.IsNullOrEmpty(valor) ? null : valor;
}
