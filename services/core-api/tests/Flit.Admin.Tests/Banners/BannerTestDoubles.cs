using Flit.Admin.Application.Banners.Ports;
using Flit.Admin.Domain.Banners;
using Flit.Admin.Domain.Common;

namespace Flit.Admin.Tests.Banners;

/// <summary>
/// Doble de prueba de <see cref="IBannerRepository"/>, acotado a los metodos de lectura publica
/// (HU #12240: <see cref="ListActiveAsync"/>/<see cref="GetImageRefAsync"/>). El CRUD (HU #12239)
/// se prueba en <c>BannerHandlerTests</c> contra un <c>BannerRepository</c> real sobre
/// <c>FlitDbContext</c> InMemory, no con este fake — por eso los metodos de escritura aqui
/// lanzan <see cref="NotSupportedException"/> en vez de simularse.
/// </summary>
internal sealed class FakeBannerRepository : IBannerRepository
{
    public List<ActiveBannerItem> ActiveItems { get; } = [];

    public Dictionary<Guid, BannerImageRef> ImageRefs { get; } = [];

    public int ListActiveCalls { get; private set; }

    public Task<IReadOnlyList<ActiveBannerItem>> ListActiveAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ListActiveCalls++;
        return Task.FromResult<IReadOnlyList<ActiveBannerItem>>([.. ActiveItems]);
    }

    public Task<BannerImageRef?> GetImageRefAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ImageRefs.TryGetValue(id, out var value);
        return Task.FromResult<BannerImageRef?>(value);
    }

    private static NotSupportedException OutOfScope(string member) => new(
        $"FakeBannerRepository solo cubre lectura publica (HU #12240); {member} es CRUD " +
        "(HU #12239) y se prueba en BannerHandlerTests con un BannerRepository real.");

    public Task<BannerListItem> CreateAsync(
        string name, string imageStoragePath, string imageSha256, string? linkUrl,
        DateTimeOffset? validFrom, DateTimeOffset? validUntil, Guid? createdBy,
        CancellationToken cancellationToken = default) => throw OutOfScope(nameof(CreateAsync));

    public Task<PagedResult<BannerListItem>> ListAsync(
        BannerListFilter filter, CancellationToken cancellationToken = default) =>
        throw OutOfScope(nameof(ListAsync));

    public Task<BannerListItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw OutOfScope(nameof(GetByIdAsync));

    public Task<BannerListItem?> UpdateAsync(
        Guid id, string name, string? linkUrl, DateTimeOffset? validFrom, DateTimeOffset? validUntil,
        string? imageStoragePath, string? imageSha256, Guid? updatedBy,
        CancellationToken cancellationToken = default) => throw OutOfScope(nameof(UpdateAsync));

    public Task<bool> SetActiveAsync(
        Guid id, bool isActive, Guid? updatedBy, CancellationToken cancellationToken = default) =>
        throw OutOfScope(nameof(SetActiveAsync));

    public Task<bool> SoftDeleteAsync(
        Guid id, Guid? deletedBy, CancellationToken cancellationToken = default) =>
        throw OutOfScope(nameof(SoftDeleteAsync));
}

/// <summary>
/// Doble de prueba de <see cref="IBannerImageStorage"/>, acotado a lectura (HU #12240). La
/// escritura (HU #12239) se prueba en <c>BannerHandlerTests</c> con su propio fake local.
/// </summary>
internal sealed class FakeBannerImageStorage : IBannerImageStorage
{
    public Dictionary<string, byte[]> Files { get; } = [];

    /// <summary>Cuenta de aperturas reales del binario — AC2 exige que un HIT de ETag NO sume aqui.</summary>
    public int OpenReadCalls { get; private set; }

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        OpenReadCalls++;
        return Task.FromResult<Stream?>(
            Files.TryGetValue(storagePath, out var bytes) ? new MemoryStream(bytes) : null);
    }

    public Task<StoredBannerImage> SaveAsync(
        string filename, Stream content, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "FakeBannerImageStorage solo cubre lectura publica (HU #12240); SaveAsync es CRUD " +
            "(HU #12239) y se prueba en BannerHandlerTests con su propio fake local.");
}
