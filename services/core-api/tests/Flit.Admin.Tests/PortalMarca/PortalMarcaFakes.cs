using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;

namespace Flit.Admin.Tests.PortalMarca;

/// <summary>Repositorio en memoria con las mismas reglas de versión y publicación que el SQL real (que se prueba aparte contra Postgres).</summary>
public sealed class RepositorioMarcaEnMemoria : IPortalMarcaRepository
{
    private readonly Dictionary<Guid, PortalMarcaRegistro> _marcas = [];
    private readonly Dictionary<Guid, PortalMarcaImagen> _imagenes = [];
    public HashSet<Guid> Organismos { get; } = [];
    public Dictionary<string, Guid> Codigos { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool FallarAlLeer { get; set; }
    public int Lecturas { get; private set; }

    public Task<bool> ExisteOrganismoAsync(Guid transitOfficeId, CancellationToken ct = default) => Task.FromResult(Organismos.Contains(transitOfficeId));

    public Task<Guid?> ResolverOrganismoPorCodigoAsync(string codigo, CancellationToken ct = default) =>
        Task.FromResult(Codigos.TryGetValue(codigo, out var id) ? (Guid?)id : null);

    public Task<PortalMarcaRegistro> ObtenerAsync(Guid transitOfficeId, CancellationToken ct = default)
    {
        Lecturas++;
        if (FallarAlLeer)
            throw new InvalidOperationException("falla simulada de lectura");
        return Task.FromResult(_marcas.TryGetValue(transitOfficeId, out var m) ? m : PortalMarcaRegistro.Vacio(transitOfficeId));
    }

    public async Task<PortalMarcaRegistro?> GuardarBorradorAsync(Guid o, string? nombre, BrandColors? colores, long version, Guid actor, CancellationToken ct = default)
    {
        var actual = await ObtenerAsync(o, ct);
        if (actual.RowVersion != version)
            return null;
        return _marcas[o] = actual with { Draft = actual.Draft with { PlatformName = nombre, Colors = colores }, RowVersion = actual.RowVersion + 1 };
    }

    public async Task<PortalMarcaRegistro?> PublicarAsync(Guid o, long version, Guid actor, CancellationToken ct = default)
    {
        var actual = await ObtenerAsync(o, ct);
        if (actual.RowVersion != version)
            return null;
        return _marcas[o] = actual with
        {
            Published = actual.Draft,
            PublishedVersion = actual.PublishedVersion + 1,
            PublishedAt = DateTimeOffset.UtcNow,
            PublishedBy = actor,
            RowVersion = actual.RowVersion + 1,
        };
    }

    public async Task<PortalMarcaRegistro?> RetirarAsync(Guid o, long version, Guid actor, CancellationToken ct = default)
    {
        var actual = await ObtenerAsync(o, ct);
        if (actual.RowVersion != version)
            return null;
        return _marcas[o] = actual with { Published = null, PublishedAt = null, PublishedBy = null, RowVersion = actual.RowVersion + 1 };
    }

    public async Task<(PortalMarcaImagen Imagen, PortalMarcaRegistro Marca)> AgregarImagenAsync(
        Guid o, string kind, NuevaPortalMarcaImagen imagen, Guid actor, CancellationToken ct = default)
    {
        var actual = await ObtenerAsync(o, ct);
        var version = _imagenes.Values.Count(i => i.TransitOfficeId == o && i.Kind == kind) + 1;
        var nueva = new PortalMarcaImagen(Guid.NewGuid(), o, kind, version, imagen.ContentType, imagen.StoragePath, imagen.Sha256, imagen.SizeBytes);
        _imagenes[nueva.Id] = nueva;
        var banners = actual.Draft.Banners.ToList();
        if (banners.Count > 0)
            banners[0] = banners[0] with { ImageId = nueva.Id };
        else
            banners.Add(new PortalMarcaCarruselItem(nueva.Id, PortalMarcaSnapshot.BannerAltPorDefecto, null));
        var draft = kind == PortalMarcaImagen.KindBanner ? actual.Draft with { BannerId = nueva.Id, Banners = banners } : actual.Draft with { LogoId = nueva.Id };
        return (nueva, _marcas[o] = actual with { Draft = draft, RowVersion = actual.RowVersion + 1 });
    }

    public async Task<PortalMarcaRegistro> QuitarImagenAsync(Guid o, string kind, Guid actor, CancellationToken ct = default)
    {
        var actual = await ObtenerAsync(o, ct);
        var resto = actual.Draft.Banners.Skip(1).ToList();
        var draft = kind == PortalMarcaImagen.KindBanner ? actual.Draft with { BannerId = resto.FirstOrDefault()?.ImageId, Banners = resto } : actual.Draft with { LogoId = null };
        return _marcas[o] = actual with { Draft = draft, RowVersion = actual.RowVersion + 1 };
    }

    public async Task<(PortalMarcaImagen? Imagen, PortalMarcaRegistro? Marca)> AgregarCarruselAsync(
        Guid o, NuevaPortalMarcaImagen imagen, string alt, string? linkUrl, int maximo, Guid actor, CancellationToken ct = default)
    {
        var actual = await ObtenerAsync(o, ct);
        if (actual.Draft.Carousel.Count >= maximo)
            return (null, null);
        var version = _imagenes.Values.Count(i => i.TransitOfficeId == o && i.Kind == PortalMarcaImagen.KindCarousel) + 1;
        var nueva = new PortalMarcaImagen(Guid.NewGuid(), o, PortalMarcaImagen.KindCarousel, version, imagen.ContentType, imagen.StoragePath, imagen.Sha256, imagen.SizeBytes);
        _imagenes[nueva.Id] = nueva;
        List<PortalMarcaCarruselItem> items = [.. actual.Draft.Carousel, new PortalMarcaCarruselItem(nueva.Id, alt, linkUrl)];
        return (nueva, _marcas[o] = actual with { Draft = actual.Draft with { Carousel = items }, RowVersion = actual.RowVersion + 1 });
    }

    public async Task<PortalMarcaRegistro?> ActualizarCarruselAsync(Guid o, Guid imageId, string alt, string? linkUrl, Guid actor, CancellationToken ct = default)
    {
        var actual = await ObtenerAsync(o, ct);
        if (actual.Draft.Carousel.All(i => i.ImageId != imageId))
            return null;
        var items = actual.Draft.Carousel.Select(i => i.ImageId == imageId ? i with { Alt = alt, LinkUrl = linkUrl } : i).ToList();
        return _marcas[o] = actual with { Draft = actual.Draft with { Carousel = items }, RowVersion = actual.RowVersion + 1 };
    }

    public async Task<PortalMarcaRegistro?> OrdenarCarruselAsync(Guid o, IReadOnlyList<Guid> imageIds, Guid actor, CancellationToken ct = default)
    {
        var actual = await ObtenerAsync(o, ct);
        var items = actual.Draft.Carousel;
        if (imageIds.Count != items.Count || !items.Select(i => i.ImageId).ToHashSet().SetEquals(imageIds))
            return null;
        var ordenados = imageIds.Select(g => items.First(i => i.ImageId == g)).ToList();
        return _marcas[o] = actual with { Draft = actual.Draft with { Carousel = ordenados }, RowVersion = actual.RowVersion + 1 };
    }

    public async Task<PortalMarcaRegistro?> QuitarCarruselAsync(Guid o, Guid imageId, Guid actor, CancellationToken ct = default)
    {
        var actual = await ObtenerAsync(o, ct);
        if (actual.Draft.Carousel.All(i => i.ImageId != imageId))
            return null;
        var items = actual.Draft.Carousel.Where(i => i.ImageId != imageId).ToList();
        return _marcas[o] = actual with { Draft = actual.Draft with { Carousel = items }, RowVersion = actual.RowVersion + 1 };
    }

    private PortalMarcaRegistro ConBanners(PortalMarcaRegistro actual, List<PortalMarcaCarruselItem> items) =>
        _marcas[actual.TransitOfficeId] = actual with
        {
            Draft = actual.Draft with { Banners = items, BannerId = items.FirstOrDefault()?.ImageId },
            RowVersion = actual.RowVersion + 1,
        };

    public async Task<(PortalMarcaImagen? Imagen, PortalMarcaRegistro? Marca)> AgregarBannerAsync(
        Guid o, NuevaPortalMarcaImagen imagen, string alt, string? linkUrl, int maximo, Guid actor, CancellationToken ct = default)
    {
        var actual = await ObtenerAsync(o, ct);
        if (actual.Draft.Banners.Count >= maximo)
            return (null, null);
        var version = _imagenes.Values.Count(i => i.TransitOfficeId == o && i.Kind == PortalMarcaImagen.KindBanner) + 1;
        var nueva = new PortalMarcaImagen(Guid.NewGuid(), o, PortalMarcaImagen.KindBanner, version, imagen.ContentType, imagen.StoragePath, imagen.Sha256, imagen.SizeBytes);
        _imagenes[nueva.Id] = nueva;
        return (nueva, ConBanners(actual, [.. actual.Draft.Banners, new PortalMarcaCarruselItem(nueva.Id, alt, linkUrl)]));
    }

    public async Task<PortalMarcaRegistro?> ActualizarBannerAsync(Guid o, Guid imageId, string alt, string? linkUrl, Guid actor, CancellationToken ct = default)
    {
        var actual = await ObtenerAsync(o, ct);
        if (actual.Draft.Banners.All(i => i.ImageId != imageId))
            return null;
        return ConBanners(actual, actual.Draft.Banners.Select(i => i.ImageId == imageId ? i with { Alt = alt, LinkUrl = linkUrl } : i).ToList());
    }

    public async Task<PortalMarcaRegistro?> OrdenarBannersAsync(Guid o, IReadOnlyList<Guid> imageIds, Guid actor, CancellationToken ct = default)
    {
        var actual = await ObtenerAsync(o, ct);
        var items = actual.Draft.Banners;
        if (imageIds.Count != items.Count || !items.Select(i => i.ImageId).ToHashSet().SetEquals(imageIds))
            return null;
        return ConBanners(actual, imageIds.Select(g => items.First(i => i.ImageId == g)).ToList());
    }

    public async Task<PortalMarcaRegistro?> QuitarBannerAsync(Guid o, Guid imageId, Guid actor, CancellationToken ct = default)
    {
        var actual = await ObtenerAsync(o, ct);
        if (actual.Draft.Banners.All(i => i.ImageId != imageId))
            return null;
        return ConBanners(actual, actual.Draft.Banners.Where(i => i.ImageId != imageId).ToList());
    }

    public Task<PortalMarcaImagen?> ObtenerImagenAsync(Guid imagenId, CancellationToken ct = default) =>
        Task.FromResult(_imagenes.TryGetValue(imagenId, out var i) ? i : null);

    public Task<bool> ImagenEstaPublicadaAsync(Guid imagenId, CancellationToken ct = default) =>
        Task.FromResult(_marcas.Values.Any(m => m.Published is { } p && (p.LogoId == imagenId || p.BannerId == imagenId || p.Carousel.Any(c => c.ImageId == imagenId) || p.Banners.Any(c => c.ImageId == imagenId))));

    public void Sembrar(PortalMarcaRegistro registro) => _marcas[registro.TransitOfficeId] = registro;
}

public sealed class AlmacenImagenEnMemoria : IPortalMarcaImagenStorage
{
    public Dictionary<string, byte[]> Archivos { get; } = [];

    public async Task<NuevaPortalMarcaImagen> GuardarAsync(Guid transitOfficeId, string contentType, Stream contenido, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await contenido.CopyToAsync(ms, ct);
        var ruta = "mem://" + Guid.NewGuid();
        Archivos[ruta] = ms.ToArray();
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ms.ToArray())).ToLowerInvariant();
        return new NuevaPortalMarcaImagen(contentType, ruta, sha, ms.Length);
    }

    public Task<Stream?> AbrirAsync(string storagePath, CancellationToken ct = default) =>
        Task.FromResult<Stream?>(Archivos.TryGetValue(storagePath, out var b) ? new MemoryStream(b) : null);
}

public sealed class AuditoriaEnMemoria : IAdminAuditWriter
{
    public List<AdminAuditEntry> Entradas { get; } = [];

    public Task WriteAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
    {
        Entradas.Add(entry);
        return Task.CompletedTask;
    }
}

public static class ImagenesDePrueba
{
    public static byte[] Png(int relleno = 64) => Con([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], relleno);

    public static byte[] Jpeg(int relleno = 64) => Con([0xFF, 0xD8, 0xFF, 0xE0], relleno);

    public static byte[] Webp(int relleno = 64) => Con([0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50], relleno);

    public static byte[] Con(byte[] firma, int relleno)
    {
        var b = new byte[firma.Length + relleno];
        firma.CopyTo(b, 0);
        return b;
    }
}
