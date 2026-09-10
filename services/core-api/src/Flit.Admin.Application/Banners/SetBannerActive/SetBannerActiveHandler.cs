using Flit.Admin.Domain.Banners;

namespace Flit.Admin.Application.Banners.SetBannerActive;

public sealed class SetBannerActiveHandler
{
    private readonly IBannerRepository _repository;

    public SetBannerActiveHandler(IBannerRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<SetBannerActiveResult> HandleAsync(SetBannerActiveCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var updated = await _repository
            .SetActiveAsync(command.Id, command.IsActive, command.UpdatedBy, cancellationToken)
            .ConfigureAwait(false);

        return updated ? SetBannerActiveResult.Updated() : SetBannerActiveResult.NotFound();
    }
}
