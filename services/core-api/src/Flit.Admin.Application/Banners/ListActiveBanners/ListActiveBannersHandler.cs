using Flit.Admin.Domain.Banners;

namespace Flit.Admin.Application.Banners.ListActiveBanners;

/// <summary>
/// Caso de uso del endpoint publico de banners activos (HU #12240, AC1). Sin filtro de tenant
/// (ADR-0058: el set es global, el mismo para todos los tenants); sin autenticacion (el dato no
/// es sensible y se consume desde cualquier tenant o rol).
/// </summary>
public sealed class ListActiveBannersHandler
{
    private readonly IBannerRepository _repository;

    public ListActiveBannersHandler(IBannerRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ListActiveBannersResult> HandleAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var items = await _repository.ListActiveAsync(nowUtc, cancellationToken).ConfigureAwait(false);

        var data = items.Select(ActiveBannerResponse.From).ToList();

        return new ListActiveBannersResult(data);
    }
}
