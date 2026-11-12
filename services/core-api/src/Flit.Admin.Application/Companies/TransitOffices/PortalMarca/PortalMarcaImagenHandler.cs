using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;

namespace Flit.Admin.Application.Companies.TransitOffices.PortalMarca;

/// <summary>Imagen lista para servir: bytes, tipo y SHA-256 (ETag).</summary>
public sealed record PortalMarcaImagenContenido(Stream Contenido, string ContentType, string Sha256);

/// <summary>
/// HU #14376 — cargar, quitar y leer el logo y el banner del portal de un organismo. Cada carga es una versión nueva con id
/// propio y la anterior se conserva. El formato se decide por el contenido real, no por la extensión.
/// </summary>
public sealed class PortalMarcaImagenHandler(
    IPortalMarcaRepository repo, IPortalMarcaImagenStorage storage, PortalMarcaAdminHandler marca)
{
    public async Task<PortalMarcaResultado<PortalMarcaDto>> SubirAsync(
        Guid transitOfficeId, string kind, string? nombreArchivo, Stream archivo, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(archivo);
        var limite = PortalMarcaReglas.LimiteBytes(kind);

        // Se lee un byte más que el tope: así se detecta el exceso sin cargar el archivo completo en memoria.
        var memoria = new MemoryStream();
        var excedio = false;
        var buffer = new byte[16 * 1024];
        int leidos;
        while ((leidos = await archivo.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (memoria.Length + leidos > limite)
            {
                excedio = true;
                break;
            }

            memoria.Write(buffer, 0, leidos);
        }

        await using (memoria.ConfigureAwait(false))
        {
            memoria.Position = 0;
            var (error, tipo) = await PortalMarcaReglas.ValidarImagenAsync(kind, nombreArchivo, memoria, excedio, ct).ConfigureAwait(false);
            if (error is not null)
                return PortalMarcaAdminHandler.Falla(error);

            memoria.Position = 0;
            var guardada = await storage.GuardarAsync(transitOfficeId, tipo!, memoria, ct).ConfigureAwait(false);
            var anterior = await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
            var (_, registro) = await repo.AgregarImagenAsync(transitOfficeId, kind, guardada, actor, ct).ConfigureAwait(false);

            var anteriorId = kind == PortalMarcaImagen.KindBanner ? anterior.Draft.BannerId : anterior.Draft.LogoId;
            await marca.EscribirAuditoriaAsync(
                $"portal_marca_{kind}", actor, transitOfficeId,
                anteriorId is null ? null : PortalMarcaAdminHandler.Resumen(anterior.Draft),
                PortalMarcaAdminHandler.Resumen(registro.Draft), ct).ConfigureAwait(false);
            return PortalMarcaResultado<PortalMarcaDto>.De(PortalMarcaAdminHandler.ADto(registro));
        }
    }

    public async Task<PortalMarcaDto> QuitarAsync(Guid transitOfficeId, string kind, Guid actor, CancellationToken ct = default)
    {
        var anterior = await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
        var registro = await repo.QuitarImagenAsync(transitOfficeId, kind, actor, ct).ConfigureAwait(false);
        var tenia = kind == PortalMarcaImagen.KindBanner ? anterior.Draft.BannerId : anterior.Draft.LogoId;
        if (tenia is not null)
            await marca.EscribirAuditoriaAsync(
                $"portal_marca_{kind}_quitado", actor, transitOfficeId,
                PortalMarcaAdminHandler.Resumen(anterior.Draft), PortalMarcaAdminHandler.Resumen(registro.Draft), ct).ConfigureAwait(false);
        return PortalMarcaAdminHandler.ADto(registro);
    }

    // ── Carrusel de la columna lateral (solo organismos; el ámbito web no lo admite) ─────────────────────────────────

    private static readonly PortalMarcaError CarruselWeb =
        new(400, PortalMarcaErrores.Datos, "El carrusel solo está disponible en el portal de un organismo.");

    private static PortalMarcaResultado<PortalMarcaDto> NoEncontradaEnCarrusel() =>
        PortalMarcaResultado<PortalMarcaDto>.Falla(404, PortalMarcaErrores.NoEncontrada, "La imagen no está en el carrusel.");

    private static PortalMarcaResultado<PortalMarcaDto> CarruselLleno() =>
        PortalMarcaResultado<PortalMarcaDto>.Falla(
            409, PortalMarcaErrores.CarruselLleno,
            $"El carrusel ya tiene {PortalMarcaSnapshot.CarruselMaximo} imágenes. Quite una para agregar otra.");

    private static PortalMarcaResultado<PortalMarcaDto> OrdenInvalido() =>
        PortalMarcaResultado<PortalMarcaDto>.Falla(
            400, PortalMarcaErrores.Datos, "El nuevo orden debe incluir cada imagen del carrusel exactamente una vez.");

    public async Task<PortalMarcaResultado<PortalMarcaDto>> AgregarCarruselAsync(
        Guid transitOfficeId, string? nombreArchivo, Stream archivo, string? alt, string? linkUrl, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(archivo);
        if (transitOfficeId == PortalMarcaAmbito.Web)
            return PortalMarcaAdminHandler.Falla(CarruselWeb);
        var (errorItem, texto, enlace) = PortalMarcaReglas.ValidarItemCarrusel(alt, linkUrl);
        if (errorItem is not null)
            return PortalMarcaAdminHandler.Falla(errorItem);

        var anterior = await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
        if (anterior.Draft.Carousel.Count >= PortalMarcaSnapshot.CarruselMaximo)
            return CarruselLleno();

        const string kind = PortalMarcaImagen.KindCarousel;
        var limite = PortalMarcaReglas.LimiteBytes(kind);
        var memoria = new MemoryStream();
        var excedio = false;
        var buffer = new byte[16 * 1024];
        int leidos;
        while ((leidos = await archivo.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (memoria.Length + leidos > limite)
            {
                excedio = true;
                break;
            }

            memoria.Write(buffer, 0, leidos);
        }

        await using (memoria.ConfigureAwait(false))
        {
            memoria.Position = 0;
            var (error, tipo) = await PortalMarcaReglas.ValidarImagenAsync(kind, nombreArchivo, memoria, excedio, ct).ConfigureAwait(false);
            if (error is not null)
                return PortalMarcaAdminHandler.Falla(error);

            memoria.Position = 0;
            var guardada = await storage.GuardarAsync(transitOfficeId, tipo!, memoria, ct).ConfigureAwait(false);
            var (_, registro) = await repo.AgregarCarruselAsync(
                transitOfficeId, guardada, texto, enlace, PortalMarcaSnapshot.CarruselMaximo, actor, ct).ConfigureAwait(false);
            if (registro is null)
                return CarruselLleno();

            await marca.EscribirAuditoriaAsync(
                "portal_marca_carrusel", actor, transitOfficeId,
                PortalMarcaAdminHandler.Resumen(anterior.Draft), PortalMarcaAdminHandler.Resumen(registro.Draft), ct).ConfigureAwait(false);
            return PortalMarcaResultado<PortalMarcaDto>.De(PortalMarcaAdminHandler.ADto(registro));
        }
    }

    public async Task<PortalMarcaResultado<PortalMarcaDto>> ActualizarCarruselAsync(
        Guid transitOfficeId, Guid imageId, string? alt, string? linkUrl, Guid actor, CancellationToken ct = default)
    {
        if (transitOfficeId == PortalMarcaAmbito.Web)
            return PortalMarcaAdminHandler.Falla(CarruselWeb);
        var (errorItem, texto, enlace) = PortalMarcaReglas.ValidarItemCarrusel(alt, linkUrl);
        if (errorItem is not null)
            return PortalMarcaAdminHandler.Falla(errorItem);

        var anterior = await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
        var registro = await repo.ActualizarCarruselAsync(transitOfficeId, imageId, texto, enlace, actor, ct).ConfigureAwait(false);
        if (registro is null)
            return NoEncontradaEnCarrusel();

        await marca.EscribirAuditoriaAsync(
            "portal_marca_carrusel_editado", actor, transitOfficeId,
            PortalMarcaAdminHandler.Resumen(anterior.Draft), PortalMarcaAdminHandler.Resumen(registro.Draft), ct).ConfigureAwait(false);
        return PortalMarcaResultado<PortalMarcaDto>.De(PortalMarcaAdminHandler.ADto(registro));
    }

    public async Task<PortalMarcaResultado<PortalMarcaDto>> OrdenarCarruselAsync(
        Guid transitOfficeId, IReadOnlyList<Guid>? imageIds, Guid actor, CancellationToken ct = default)
    {
        if (transitOfficeId == PortalMarcaAmbito.Web)
            return PortalMarcaAdminHandler.Falla(CarruselWeb);
        if (imageIds is null || imageIds.Distinct().Count() != imageIds.Count)
            return OrdenInvalido();

        var anterior = await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
        var registro = await repo.OrdenarCarruselAsync(transitOfficeId, imageIds, actor, ct).ConfigureAwait(false);
        if (registro is null)
            return OrdenInvalido();

        await marca.EscribirAuditoriaAsync(
            "portal_marca_carrusel_orden", actor, transitOfficeId,
            PortalMarcaAdminHandler.Resumen(anterior.Draft), PortalMarcaAdminHandler.Resumen(registro.Draft), ct).ConfigureAwait(false);
        return PortalMarcaResultado<PortalMarcaDto>.De(PortalMarcaAdminHandler.ADto(registro));
    }

    public async Task<PortalMarcaResultado<PortalMarcaDto>> QuitarCarruselAsync(
        Guid transitOfficeId, Guid imageId, Guid actor, CancellationToken ct = default)
    {
        if (transitOfficeId == PortalMarcaAmbito.Web)
            return PortalMarcaAdminHandler.Falla(CarruselWeb);

        var anterior = await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
        var registro = await repo.QuitarCarruselAsync(transitOfficeId, imageId, actor, ct).ConfigureAwait(false);
        if (registro is null)
            return NoEncontradaEnCarrusel();

        await marca.EscribirAuditoriaAsync(
            "portal_marca_carrusel_quitado", actor, transitOfficeId,
            PortalMarcaAdminHandler.Resumen(anterior.Draft), PortalMarcaAdminHandler.Resumen(registro.Draft), ct).ConfigureAwait(false);
        return PortalMarcaResultado<PortalMarcaDto>.De(PortalMarcaAdminHandler.ADto(registro));
    }

    // ── Banners del portal del organismo (hasta 6; el banner único legacy es el primero) ───────────────────────────

    private static readonly PortalMarcaError BannersWeb =
        new(400, PortalMarcaErrores.Datos, "Los banners solo están disponibles en el portal de un organismo.");

    private static PortalMarcaResultado<PortalMarcaDto> NoEncontradoEnBanners() =>
        PortalMarcaResultado<PortalMarcaDto>.Falla(404, PortalMarcaErrores.NoEncontrada, "El banner no está en la lista.");

    private static PortalMarcaResultado<PortalMarcaDto> BannersLleno() =>
        PortalMarcaResultado<PortalMarcaDto>.Falla(
            409, PortalMarcaErrores.BannersLleno,
            $"Ya hay {PortalMarcaSnapshot.BannersMaximo} banners. Quite uno para agregar otro.");

    private static PortalMarcaResultado<PortalMarcaDto> OrdenBannersInvalido() =>
        PortalMarcaResultado<PortalMarcaDto>.Falla(
            400, PortalMarcaErrores.Datos, "El nuevo orden debe incluir cada banner exactamente una vez.");

    public async Task<PortalMarcaResultado<PortalMarcaDto>> AgregarBannerAsync(
        Guid transitOfficeId, string? nombreArchivo, Stream archivo, string? alt, string? linkUrl, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(archivo);
        if (transitOfficeId == PortalMarcaAmbito.Web)
            return PortalMarcaAdminHandler.Falla(BannersWeb);
        var (errorItem, texto, enlace) = PortalMarcaReglas.ValidarItemCarrusel(alt, linkUrl);
        if (errorItem is not null)
            return PortalMarcaAdminHandler.Falla(errorItem);

        var anterior = await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
        if (anterior.Draft.Banners.Count >= PortalMarcaSnapshot.BannersMaximo)
            return BannersLleno();

        const string kind = PortalMarcaImagen.KindBanner;
        var limite = PortalMarcaReglas.LimiteBytes(kind);
        var memoria = new MemoryStream();
        var excedio = false;
        var buffer = new byte[16 * 1024];
        int leidos;
        while ((leidos = await archivo.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (memoria.Length + leidos > limite)
            {
                excedio = true;
                break;
            }

            memoria.Write(buffer, 0, leidos);
        }

        await using (memoria.ConfigureAwait(false))
        {
            memoria.Position = 0;
            var (error, tipo) = await PortalMarcaReglas.ValidarImagenAsync(kind, nombreArchivo, memoria, excedio, ct).ConfigureAwait(false);
            if (error is not null)
                return PortalMarcaAdminHandler.Falla(error);

            memoria.Position = 0;
            var guardada = await storage.GuardarAsync(transitOfficeId, tipo!, memoria, ct).ConfigureAwait(false);
            var (_, registro) = await repo.AgregarBannerAsync(
                transitOfficeId, guardada, texto, enlace, PortalMarcaSnapshot.BannersMaximo, actor, ct).ConfigureAwait(false);
            if (registro is null)
                return BannersLleno();

            await marca.EscribirAuditoriaAsync(
                "portal_marca_banners", actor, transitOfficeId,
                PortalMarcaAdminHandler.Resumen(anterior.Draft), PortalMarcaAdminHandler.Resumen(registro.Draft), ct).ConfigureAwait(false);
            return PortalMarcaResultado<PortalMarcaDto>.De(PortalMarcaAdminHandler.ADto(registro));
        }
    }

    public async Task<PortalMarcaResultado<PortalMarcaDto>> ActualizarBannerAsync(
        Guid transitOfficeId, Guid imageId, string? alt, string? linkUrl, Guid actor, CancellationToken ct = default)
    {
        if (transitOfficeId == PortalMarcaAmbito.Web)
            return PortalMarcaAdminHandler.Falla(BannersWeb);
        var (errorItem, texto, enlace) = PortalMarcaReglas.ValidarItemCarrusel(alt, linkUrl);
        if (errorItem is not null)
            return PortalMarcaAdminHandler.Falla(errorItem);

        var anterior = await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
        var registro = await repo.ActualizarBannerAsync(transitOfficeId, imageId, texto, enlace, actor, ct).ConfigureAwait(false);
        if (registro is null)
            return NoEncontradoEnBanners();

        await marca.EscribirAuditoriaAsync(
            "portal_marca_banners_editado", actor, transitOfficeId,
            PortalMarcaAdminHandler.Resumen(anterior.Draft), PortalMarcaAdminHandler.Resumen(registro.Draft), ct).ConfigureAwait(false);
        return PortalMarcaResultado<PortalMarcaDto>.De(PortalMarcaAdminHandler.ADto(registro));
    }

    public async Task<PortalMarcaResultado<PortalMarcaDto>> OrdenarBannersAsync(
        Guid transitOfficeId, IReadOnlyList<Guid>? imageIds, Guid actor, CancellationToken ct = default)
    {
        if (transitOfficeId == PortalMarcaAmbito.Web)
            return PortalMarcaAdminHandler.Falla(BannersWeb);
        if (imageIds is null || imageIds.Distinct().Count() != imageIds.Count)
            return OrdenBannersInvalido();

        var anterior = await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
        var registro = await repo.OrdenarBannersAsync(transitOfficeId, imageIds, actor, ct).ConfigureAwait(false);
        if (registro is null)
            return OrdenBannersInvalido();

        await marca.EscribirAuditoriaAsync(
            "portal_marca_banners_orden", actor, transitOfficeId,
            PortalMarcaAdminHandler.Resumen(anterior.Draft), PortalMarcaAdminHandler.Resumen(registro.Draft), ct).ConfigureAwait(false);
        return PortalMarcaResultado<PortalMarcaDto>.De(PortalMarcaAdminHandler.ADto(registro));
    }

    public async Task<PortalMarcaResultado<PortalMarcaDto>> QuitarBannerAsync(
        Guid transitOfficeId, Guid imageId, Guid actor, CancellationToken ct = default)
    {
        if (transitOfficeId == PortalMarcaAmbito.Web)
            return PortalMarcaAdminHandler.Falla(BannersWeb);

        var anterior = await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
        var registro = await repo.QuitarBannerAsync(transitOfficeId, imageId, actor, ct).ConfigureAwait(false);
        if (registro is null)
            return NoEncontradoEnBanners();

        await marca.EscribirAuditoriaAsync(
            "portal_marca_banners_quitado", actor, transitOfficeId,
            PortalMarcaAdminHandler.Resumen(anterior.Draft), PortalMarcaAdminHandler.Resumen(registro.Draft), ct).ConfigureAwait(false);
        return PortalMarcaResultado<PortalMarcaDto>.De(PortalMarcaAdminHandler.ADto(registro));
    }

    /// <summary>Metadatos de una imagen (también de borrador). El endpoint verifica el alcance con <c>TransitOfficeId</c> antes de abrirla.</summary>
    public Task<PortalMarcaImagen?> BuscarAsync(Guid imagenId, CancellationToken ct = default) =>
        repo.ObtenerImagenAsync(imagenId, ct);

    public async Task<PortalMarcaImagenContenido?> AbrirAsync(PortalMarcaImagen imagen, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imagen);
        var stream = await storage.AbrirAsync(imagen.StoragePath, ct).ConfigureAwait(false);
        return stream is null ? null : new PortalMarcaImagenContenido(stream, imagen.ContentType, imagen.Sha256);
    }
}
