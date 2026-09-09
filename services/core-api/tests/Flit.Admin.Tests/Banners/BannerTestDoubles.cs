using Flit.Admin.Application.Banners.Ports;
using Flit.Admin.Domain.Banners;

namespace Flit.Admin.Tests.Banners;

/// <summary>Doble de prueba de <see cref="IBannerRepository"/> (HU #12240).</summary>
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
}

/// <summary>Doble de prueba de <see cref="IBannerImageStorage"/> (HU #12240).</summary>
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
}
