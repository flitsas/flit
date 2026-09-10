using Flit.Admin.Application.Banners.Ports;
using Flit.Admin.Domain.Banners;

namespace Flit.Admin.Application.Banners.CreateBanner;

public sealed class CreateBannerHandler
{
    private readonly IBannerRepository _repository;
    private readonly IBannerImageStorage _imageStorage;
    private readonly TimeProvider _timeProvider;

    public CreateBannerHandler(IBannerRepository repository, IBannerImageStorage imageStorage, TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _imageStorage = imageStorage ?? throw new ArgumentNullException(nameof(imageStorage));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<CreateBannerResult> HandleAsync(CreateBannerCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = command.Name.Trim();
        var linkUrl = string.IsNullOrWhiteSpace(command.LinkUrl) ? null : command.LinkUrl.Trim();

        var payloadError = BannerValidator.ValidatePayload(name, linkUrl, command.ValidFrom, command.ValidUntil);
        if (payloadError is not null)
        {
            return CreateBannerResult.Invalid(payloadError);
        }

        var imageError = BannerValidator.ValidateImage(command.ImageContentType, command.ImageSizeBytes);
        if (imageError is not null)
        {
            return CreateBannerResult.Invalid(imageError);
        }

        var stored = await _imageStorage
            .SaveAsync(command.ImageFilename, command.ImageContent, cancellationToken)
            .ConfigureAwait(false);

        var created = await _repository
            .CreateAsync(
                name, stored.StoragePath, stored.Sha256, linkUrl,
                command.ValidFrom, command.ValidUntil, command.CreatedBy, cancellationToken)
            .ConfigureAwait(false);

        return CreateBannerResult.Success(BannerResponse.From(created, _timeProvider.GetUtcNow()));
    }
}
