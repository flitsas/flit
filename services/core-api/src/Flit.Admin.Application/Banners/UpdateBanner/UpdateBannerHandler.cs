using Flit.Admin.Application.Banners.Ports;
using Flit.Admin.Domain.Banners;

namespace Flit.Admin.Application.Banners.UpdateBanner;

public sealed class UpdateBannerHandler
{
    private readonly IBannerRepository _repository;
    private readonly IBannerImageStorage _imageStorage;
    private readonly TimeProvider _timeProvider;

    public UpdateBannerHandler(IBannerRepository repository, IBannerImageStorage imageStorage, TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _imageStorage = imageStorage ?? throw new ArgumentNullException(nameof(imageStorage));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<UpdateBannerResult> HandleAsync(UpdateBannerCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await _repository.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return UpdateBannerResult.NotFound();
        }

        var name = command.Name.Trim();
        var linkUrl = string.IsNullOrWhiteSpace(command.LinkUrl) ? null : command.LinkUrl.Trim();

        var payloadError = BannerValidator.ValidatePayload(name, linkUrl, command.ValidFrom, command.ValidUntil);
        if (payloadError is not null)
        {
            return UpdateBannerResult.ValidationFailed(payloadError);
        }

        string? newStoragePath = null;
        string? newSha256 = null;

        var replacesImage = command.ImageContent is not null;
        if (replacesImage)
        {
            var imageError = BannerValidator.ValidateImage(command.ImageContentType, command.ImageSizeBytes ?? 0);
            if (imageError is not null)
            {
                return UpdateBannerResult.ValidationFailed(imageError);
            }

            var stored = await _imageStorage
                .SaveAsync(command.ImageFilename ?? string.Empty, command.ImageContent!, cancellationToken)
                .ConfigureAwait(false);
            newStoragePath = stored.StoragePath;
            newSha256 = stored.Sha256;
        }

        var updated = await _repository
            .UpdateAsync(
                command.Id, name, linkUrl, command.ValidFrom, command.ValidUntil,
                newStoragePath, newSha256, command.UpdatedBy, cancellationToken)
            .ConfigureAwait(false);

        return updated is null
            ? UpdateBannerResult.NotFound()
            : UpdateBannerResult.Updated(BannerResponse.From(updated, _timeProvider.GetUtcNow()));
    }
}
