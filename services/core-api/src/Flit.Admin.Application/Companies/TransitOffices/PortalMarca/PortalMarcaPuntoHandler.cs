using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;
using Microsoft.Extensions.Logging;

namespace Flit.Admin.Application.Companies.TransitOffices.PortalMarca;

public sealed record MarcaPuntoColoresDto(string Primario, string Secundario, string SobrePrimario);

/// <summary>
/// Marca que recibe el portal del punto. Solo lo publicado: nunca el borrador, la versión de fila ni datos internos. Las imágenes
/// van por id; el BFF arma la URL pública.
/// </summary>
public sealed record MarcaPuntoDto(string Origen, string? Nombre, MarcaPuntoColoresDto? Colores, Guid? LogoId, Guid? BannerId, int Version)
{
    /// <summary>Carrusel publicado, en orden. Solo el origen organismo lo llena; web y FLIT van vacíos.</summary>
    public IReadOnlyList<PortalMarcaCarruselItem> Carrusel { get; init; } = [];

    /// <summary>Banners principales publicados, en orden (el primero coincide con <c>BannerId</c>). Solo el origen organismo los llena.</summary>
    public IReadOnlyList<PortalMarcaCarruselItem> Banners { get; init; } = [];

    public const string OrigenOrganismo = "organismo";
    public const string OrigenWeb = "web";
    public const string OrigenFlit = "flit";

    public static readonly MarcaPuntoDto Flit = new(OrigenFlit, null, null, null, null, 0);
}

/// <summary>Imagen publicada lista para entregar al portal.</summary>
public sealed record MarcaPuntoImagenDto(bool Encontrada, bool NoModificada, string Sha256, string ContentType, Stream? Contenido);

/// <summary>
/// HU #14378 — entrega de la marca publicada al portal. Sin caché por tiempo (ni los 60 s de marca blanca): cada lectura va a
/// la base, así lo recién publicado o retirado se ve en la siguiente consulta. Ante código inválido, organismo sin marca o
/// cualquier falla responde la marca FLIT, nunca un error.
/// </summary>
public sealed class PortalMarcaPuntoHandler(IPortalMarcaRepository repo, IPortalMarcaImagenStorage storage, ILogger<PortalMarcaPuntoHandler> logger)
{
    private const int CodigoMaxLargo = 20;

    public async Task<MarcaPuntoDto> ObtenerAsync(string? codigo, CancellationToken ct = default)
    {
        var buscado = (codigo ?? string.Empty).Trim();
        if (buscado.Length == 0 || buscado.Length > CodigoMaxLargo)
            return MarcaPuntoDto.Flit;

        try
        {
            if (await repo.ResolverOrganismoPorCodigoAsync(buscado, ct).ConfigureAwait(false) is not { } organismo)
                return MarcaPuntoDto.Flit;

            return DePublicada(await repo.ObtenerAsync(organismo, ct).ConfigureAwait(false), MarcaPuntoDto.OrigenOrganismo, conBanner: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
#pragma warning disable CA1848 // Mensaje fijo y sin datos internos; la ruta es de alta frecuencia solo en falla.
            logger.LogWarning(ex, "No se pudo leer la marca del portal del punto; se entrega la marca FLIT.");
#pragma warning restore CA1848
            return MarcaPuntoDto.Flit;
        }
    }

    /// <summary>
    /// HU #14401 — marca publicada de la web de persona natural. Mismas garantías que la del punto: sin caché, sin borrador, y ante
    /// marca sin publicar, incompleta o una falla responde la marca FLIT. Sin banner: los de la web viven en Contenido.
    /// </summary>
    public async Task<MarcaPuntoDto> ObtenerWebAsync(CancellationToken ct = default)
    {
        try
        {
            return DePublicada(await repo.ObtenerAsync(PortalMarcaAmbito.Web, ct).ConfigureAwait(false), MarcaPuntoDto.OrigenWeb, conBanner: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
#pragma warning disable CA1848 // Mensaje fijo y sin datos internos.
            logger.LogWarning(ex, "No se pudo leer la marca de la web de persona natural; se entrega la marca FLIT.");
#pragma warning restore CA1848
            return MarcaPuntoDto.Flit;
        }
    }

    private static MarcaPuntoDto DePublicada(PortalMarcaRegistro marca, string origen, bool conBanner)
    {
        var p = marca.Published;
        if (p is null || p.MissingFields().Count > 0)
            return MarcaPuntoDto.Flit;

        return new MarcaPuntoDto(
            origen,
            p.PlatformName,
            new MarcaPuntoColoresDto(p.Colors!.Primary, p.Colors.Secondary, p.Colors.OnPrimary),
            p.LogoId,
            conBanner ? p.BannerId : null,
            marca.PublishedVersion)
        {
            Carrusel = conBanner ? p.Carousel : [],
            Banners = conBanner ? p.Banners : [],
        };
    }

    /// <summary>Solo imágenes del publicado vigente. Una de borrador, de una versión retirada o inexistente es «no encontrada».</summary>
    public async Task<MarcaPuntoImagenDto> ObtenerImagenAsync(Guid imagenId, string? ifNoneMatch, CancellationToken ct = default)
    {
        var noEncontrada = new MarcaPuntoImagenDto(false, false, string.Empty, string.Empty, null);
        if (!await repo.ImagenEstaPublicadaAsync(imagenId, ct).ConfigureAwait(false))
            return noEncontrada;
        if (await repo.ObtenerImagenAsync(imagenId, ct).ConfigureAwait(false) is not { } imagen)
            return noEncontrada;
        if (string.Equals(ifNoneMatch?.Trim().Trim('"'), imagen.Sha256, StringComparison.OrdinalIgnoreCase))
            return new MarcaPuntoImagenDto(true, true, imagen.Sha256, imagen.ContentType, null);

        var contenido = await storage.AbrirAsync(imagen.StoragePath, ct).ConfigureAwait(false);
        return contenido is null ? noEncontrada : new MarcaPuntoImagenDto(true, false, imagen.Sha256, imagen.ContentType, contenido);
    }
}
