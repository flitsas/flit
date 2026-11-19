using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;

namespace Flit.Admin.Application.Companies.TransitOffices.PortalMarca;

public sealed record PortalMarcaColoresDto(string Primary, string Secondary, string OnPrimary);

/// <summary>Elemento del carrusel dentro del borrador o del publicado (sin URL: esa solo la lleva el <c>carousel</c> de primer nivel).</summary>
public sealed record PortalMarcaCarruselSnapshotItemDto(Guid ImageId, string Alt, string? LinkUrl);

public sealed record PortalMarcaSnapshotDto(
    string? PlatformName, PortalMarcaColoresDto? Colors, Guid? LogoId, Guid? BannerId,
    IReadOnlyList<PortalMarcaCarruselSnapshotItemDto> Carousel,
    IReadOnlyList<PortalMarcaCarruselSnapshotItemDto> Banners);

/// <summary>Imagen del carrusel del borrador, con la URL administrativa de la imagen (como <c>bannerUrl</c>).</summary>
public sealed record PortalMarcaCarruselItemDto(Guid ImageId, string ImageUrl, string Alt, string? LinkUrl);

public sealed record PortalMarcaCompletitudDto(bool IsComplete, IReadOnlyList<string> Missing);

public sealed record PortalMarcaPublicadoPorDto(Guid UserId);

/// <summary>Forma de <c>PortalMarca</c> del contrato de la Épica #14297. El borrador y las URL de imagen son solo de la administración.</summary>
public sealed record PortalMarcaDto(
    Guid TransitOfficeId,
    PortalMarcaSnapshotDto Draft,
    PortalMarcaSnapshotDto? Published,
    int PublishedVersion,
    DateTimeOffset? PublishedAt,
    PortalMarcaPublicadoPorDto? PublishedBy,
    bool HasUnpublishedChanges,
    string? LogoUrl,
    string? BannerUrl,
    PortalMarcaCompletitudDto Completeness,
    long RowVersion,
    IReadOnlyList<PortalMarcaCarruselItemDto> Carousel,
    IReadOnlyList<PortalMarcaCarruselItemDto> Banners);

public sealed record GuardarPortalMarcaRequest(string? PlatformName, PortalMarcaColoresDto? Colors, long RowVersion);

public sealed record VersionPortalMarcaRequest(long RowVersion);

/// <summary>Cuerpo de <c>PUT /carrusel/{imageId}</c>.</summary>
public sealed record ActualizarCarruselRequest(string? Alt, string? LinkUrl);

/// <summary>Cuerpo de <c>PUT /banners/{imageId}</c>.</summary>
public sealed record ActualizarBannerRequest(string? Alt, string? LinkUrl);

/// <summary>Cuerpo de <c>PUT /banners/orden</c>: ids de banner en el nuevo orden (permutación exacta de los actuales).</summary>
public sealed record OrdenBannersRequest(IReadOnlyList<string>? ImageIds);

/// <summary>Cuerpo de <c>PUT /carrusel/orden</c>: ids de imagen en el nuevo orden (permutación exacta de las actuales).</summary>
public sealed record OrdenCarruselRequest(IReadOnlyList<string>? ImageIds);

/// <summary>
/// HU #14375 — leer, guardar el borrador, publicar y retirar la marca del portal de un organismo. El alcance (organismo propio o
/// SuperAdmin) lo resuelve el endpoint; aquí solo hay reglas de negocio. Toda escritura exige la <c>rowVersion</c> que el editor leyó.
/// </summary>
public sealed class PortalMarcaAdminHandler(IPortalMarcaRepository repo, IAdminAuditWriter audit)
{
    public const string RutaImagenes = "/api/v1/admin/ot/portal-marca/imagenes/";

    public async Task<PortalMarcaDto> ObtenerAsync(Guid transitOfficeId, CancellationToken ct = default) =>
        ADto(await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false));

    public async Task<PortalMarcaResultado<PortalMarcaDto>> GuardarAsync(
        Guid transitOfficeId, GuardarPortalMarcaRequest request, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var nombre = request.PlatformName is null ? null : BrandingValidation.NormalizeName(request.PlatformName);
        var colores = request.Colors is null
            ? null
            : new BrandColors(request.Colors.Primary ?? string.Empty, request.Colors.Secondary ?? string.Empty, request.Colors.OnPrimary ?? string.Empty);
        if (PortalMarcaReglas.ValidarFormato(nombre, colores) is { } invalido)
            return Falla(invalido);
        if (colores is not null)
            colores = new BrandColors(
                BrandingValidation.NormalizeHexColor(colores.Primary),
                BrandingValidation.NormalizeHexColor(colores.Secondary),
                BrandingValidation.NormalizeHexColor(colores.OnPrimary));

        var anterior = await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
        var guardada = await repo.GuardarBorradorAsync(transitOfficeId, nombre, colores, request.RowVersion, actor, ct).ConfigureAwait(false);
        if (guardada is null)
            return Conflicto();

        await EscribirAuditoriaAsync("portal_marca_borrador", actor, transitOfficeId, Resumen(anterior.Draft), Resumen(guardada.Draft), ct).ConfigureAwait(false);
        return PortalMarcaResultado<PortalMarcaDto>.De(ADto(guardada));
    }

    public async Task<PortalMarcaResultado<PortalMarcaDto>> PublicarAsync(
        Guid transitOfficeId, long rowVersion, Guid actor, CancellationToken ct = default)
    {
        var actual = await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
        if (actual.RowVersion != rowVersion)
            return Conflicto();
        if (PortalMarcaReglas.ValidarParaPublicar(actual.Draft) is { } invalido)
            return Falla(invalido);

        var publicada = await repo.PublicarAsync(transitOfficeId, rowVersion, actor, ct).ConfigureAwait(false);
        if (publicada is null)
            return Conflicto();

        await EscribirAuditoriaAsync(
            "portal_marca_publicada", actor, transitOfficeId,
            actual.Published is null ? null : Resumen(actual.Published, actual.PublishedVersion),
            Resumen(publicada.Published ?? publicada.Draft, publicada.PublishedVersion), ct).ConfigureAwait(false);
        return PortalMarcaResultado<PortalMarcaDto>.De(ADto(publicada));
    }

    public async Task<PortalMarcaResultado<PortalMarcaDto>> RetirarAsync(
        Guid transitOfficeId, long rowVersion, Guid actor, CancellationToken ct = default)
    {
        var actual = await repo.ObtenerAsync(transitOfficeId, ct).ConfigureAwait(false);
        if (actual.RowVersion != rowVersion)
            return Conflicto();
        if (actual.Published is null)
            return PortalMarcaResultado<PortalMarcaDto>.De(ADto(actual));

        var retirada = await repo.RetirarAsync(transitOfficeId, rowVersion, actor, ct).ConfigureAwait(false);
        if (retirada is null)
            return Conflicto();

        await EscribirAuditoriaAsync("portal_marca_retirada", actor, transitOfficeId, Resumen(actual.Published, actual.PublishedVersion), null, ct).ConfigureAwait(false);
        return PortalMarcaResultado<PortalMarcaDto>.De(ADto(retirada));
    }

    // ── Soporte (compartido con el manejador de imágenes) ──────────────────────────────────────────────────────────

    internal static PortalMarcaDto ADto(PortalMarcaRegistro r)
    {
        var faltan = r.Draft.MissingFields();
        return new PortalMarcaDto(
            r.TransitOfficeId,
            ADto(r.Draft),
            r.Published is null ? null : ADto(r.Published),
            r.PublishedVersion,
            r.PublishedAt,
            r.PublishedBy is { } quien ? new PortalMarcaPublicadoPorDto(quien) : null,
            r.HasUnpublishedChanges,
            r.Draft.LogoId is { } logo ? RutaImagenes + logo : null,
            r.Draft.BannerId is { } banner ? RutaImagenes + banner : null,
            new PortalMarcaCompletitudDto(faltan.Count == 0, faltan),
            r.RowVersion,
            [.. r.Draft.Carousel.Select(i => new PortalMarcaCarruselItemDto(i.ImageId, RutaImagenes + i.ImageId, i.Alt, i.LinkUrl))],
            [.. r.Draft.Banners.Select(i => new PortalMarcaCarruselItemDto(i.ImageId, RutaImagenes + i.ImageId, i.Alt, i.LinkUrl))]);
    }

    internal static PortalMarcaResultado<PortalMarcaDto> Falla(PortalMarcaError e) =>
        PortalMarcaResultado<PortalMarcaDto>.Falla(e.Status, e.Code, e.Message);

    internal static PortalMarcaResultado<PortalMarcaDto> Conflicto() =>
        PortalMarcaResultado<PortalMarcaDto>.Falla(
            409, PortalMarcaErrores.Conflicto,
            "Otra persona cambió la marca mientras la editaba. Recargue para ver su cambio antes de guardar el suyo.");

    internal Task EscribirAuditoriaAsync(string entidad, Guid actor, Guid officeId, string? anterior, string? nuevo, CancellationToken ct) =>
        audit.WriteAsync(
            new AdminAuditEntry(
                TenantId: null,
                TenantType: null,
                Module: AuditVocabulary.Modules.Config,
                // La marca de la web no tiene organismo: se distingue por nombre y sin entidad objetivo.
                EntityName: officeId == PortalMarcaAmbito.Web ? entidad.Replace("portal_marca", "portal_marca_web", StringComparison.Ordinal) : entidad,
                Operation: AuditVocabulary.Operations.Update,
                Result: AuditVocabulary.Results.Success,
                ErrorCode: null,
                ActorUserId: actor,
                TargetEntityType: officeId == PortalMarcaAmbito.Web ? "PERSONA_NATURAL_MARCA_WEB" : "TRANSIT_OFFICE_PORTAL_MARCA",
                TargetEntityId: officeId == PortalMarcaAmbito.Web ? null : officeId,
                ClientIp: null,
                UserAgent: null,
                OldValue: anterior,
                NewValue: nuevo),
            ct);

    internal static string Resumen(PortalMarcaSnapshot s, int? version = null) => PersonaNaturalAuditoria.Json(
        ("platformName", s.PlatformName),
        ("primary", s.Colors?.Primary),
        ("secondary", s.Colors?.Secondary),
        ("onPrimary", s.Colors?.OnPrimary),
        ("logoId", s.LogoId),
        ("bannerId", s.BannerId),
        ("bannerImageIds", s.Banners.Count == 0 ? null : string.Join(",", s.Banners.Select(i => i.ImageId))),
        ("carouselImageIds", s.Carousel.Count == 0 ? null : string.Join(",", s.Carousel.Select(i => i.ImageId))),
        ("version", version));

    private static PortalMarcaSnapshotDto ADto(PortalMarcaSnapshot s) =>
        new(s.PlatformName, s.Colors is null ? null : new PortalMarcaColoresDto(s.Colors.Primary, s.Colors.Secondary, s.Colors.OnPrimary), s.LogoId, s.BannerId,
            [.. s.Carousel.Select(i => new PortalMarcaCarruselSnapshotItemDto(i.ImageId, i.Alt, i.LinkUrl))],
            [.. s.Banners.Select(i => new PortalMarcaCarruselSnapshotItemDto(i.ImageId, i.Alt, i.LinkUrl))]);
}
