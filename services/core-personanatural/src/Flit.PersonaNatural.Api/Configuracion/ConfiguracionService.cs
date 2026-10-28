using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Sesiones;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Configuracion;

/// <summary>
/// Contenido público del portal (HU #14073): preguntas frecuentes, banners, enlaces y contacto, tal como los PUBLICÓ el
/// superadministrador en Plataforma. core-api es el único dueño del contenido; el BFF solo lo cachea unos segundos (la misma
/// <see cref="CotizacionesOptions.CacheCatalogoSegundos"/> del catálogo: 60 s) y arma la ruta de la imagen. No lleva datos
/// bancarios ni parámetros de protección: core-api ni siquiera los entrega por este RPC.
/// </summary>
public sealed class ConfiguracionService(IFachadaConfiguracion fachada, IMemoryCache cache, IOptions<CotizacionesOptions> options)
{
    public const string RutaImagenes = SesionEndpoints.Base + "/configuracion/banners";

    private readonly CotizacionesOptions _o = options.Value;

    /// <summary>La configuración pública vigente (caché corta).</summary>
    public async Task<ConfiguracionPortalDto> ObtenerAsync(CancellationToken ct)
    {
        const string clave = "pn:configuracion";
        if (cache.TryGetValue(clave, out ConfiguracionPortalDto? existente) && existente is not null)
            return existente;

        var c = await fachada.ObtenerAsync(ct).ConfigureAwait(false);
        var dto = new ConfiguracionPortalDto(
            c.Version,
            c.PublicadoEn,
            c.Preguntas,
            [.. c.Banners.Select(b => new BannerDto(b.Id, b.Titulo, b.TextoAlternativo, b.EnlaceUrl, b.Orden, $"{RutaImagenes}/{b.Id}/imagen?v={Uri.EscapeDataString(b.ImagenSha256)}"))],
            c.Enlaces,
            c.Soporte,
            c.BorradorVigenciaDias);
        if (_o.CacheCatalogoSegundos > 0)
            cache.Set(clave, dto, TimeSpan.FromSeconds(_o.CacheCatalogoSegundos));
        return dto;
    }

    /// <summary>La imagen de un banner publicado y vigente (con su caché corta); <c>null</c> si no existe o venció.</summary>
    public async Task<ImagenBannerFachada?> ImagenAsync(string bannerId, string? ifNoneMatch, CancellationToken ct)
    {
        var clave = $"pn:configuracion:img:{bannerId}";
        if (cache.TryGetValue(clave, out ImagenBannerFachada? existente) && existente is not null)
            return string.Equals(existente.Sha256, ifNoneMatch, StringComparison.OrdinalIgnoreCase)
                ? existente with { NoModificado = true, Contenido = [] }
                : existente;

        var imagen = await fachada.ObtenerImagenAsync(bannerId, ifNoneMatch, ct).ConfigureAwait(false);
        // Solo se cachean los bytes completos (un «no modificado» no los trae).
        if (imagen is { NoModificado: false } && _o.CacheCatalogoSegundos > 0)
            cache.Set(clave, imagen, TimeSpan.FromSeconds(_o.CacheCatalogoSegundos));
        return imagen;
    }
}
