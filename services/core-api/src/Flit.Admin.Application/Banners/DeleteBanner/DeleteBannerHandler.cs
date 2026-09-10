using Flit.Admin.Domain.Banners;

namespace Flit.Admin.Application.Banners.DeleteBanner;

public sealed class DeleteBannerHandler
{
    private readonly IBannerRepository _repository;

    public DeleteBannerHandler(IBannerRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<DeleteBannerResult> HandleAsync(DeleteBannerCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!command.Confirm)
        {
            return DeleteBannerResult.ConfirmationRequired();
        }

        var existing = await _repository.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return DeleteBannerResult.NotFound();
        }

        var deleted = await _repository
            .SoftDeleteAsync(command.Id, command.DeletedBy, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? DeleteBannerResult.Deleted() : DeleteBannerResult.NotFound();
    }
}
