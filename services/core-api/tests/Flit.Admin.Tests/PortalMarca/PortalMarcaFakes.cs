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
        var draft = kind == PortalMarcaImagen.KindBanner ? actual.Draft with { BannerId = nueva.Id } : actual.Draft with { LogoId = nueva.Id };
        return (nueva, _marcas[o] = actual with { Draft = draft, RowVersion = actual.RowVersion + 1 });
    }

    public async Task<PortalMarcaRegistro> QuitarImagenAsync(Guid o, string kind, Guid actor, CancellationToken ct = default)
    {
        var actual = await ObtenerAsync(o, ct);
        var draft = kind == PortalMarcaImagen.KindBanner ? actual.Draft with { BannerId = null } : actual.Draft with { LogoId = null };
        return _marcas[o] = actual with { Draft = draft, RowVersion = actual.RowVersion + 1 };
    }

    public Task<PortalMarcaImagen?> ObtenerImagenAsync(Guid imagenId, CancellationToken ct = default) =>
        Task.FromResult(_imagenes.TryGetValue(imagenId, out var i) ? i : null);

    public Task<bool> ImagenEstaPublicadaAsync(Guid imagenId, CancellationToken ct = default) =>
        Task.FromResult(_marcas.Values.Any(m => m.Published is { } p && (p.LogoId == imagenId || p.BannerId == imagenId)));

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
