using Flit.Admin.Application.Banners.GetBannerImage;
using Flit.Admin.Domain.Companies.Branding;

namespace Flit.Admin.Application.Companies.Branding.UploadBrandLogo;

/// <summary>
/// Sube una versión nueva del logotipo (HU #12412 AC7): detecta el content-type por firma binaria
/// (reutiliza <see cref="ImageContentTypeSniffer"/>, igual que banners), mide dimensiones
/// (<see cref="ImageDimensionsReader"/>) y persiste vía <see cref="IBrandLogoStorage"/>. No aplica el
/// borrador ni publica — el cliente hace <c>PUT .../branding</c> con el <c>logoId</c> devuelto (así la
/// previsualización puede usarlo sin publicar, contrato §3). Límites de formato/peso/dimensiones son
/// responsabilidad de <see cref="IBrandAssetValidator"/> (permisivo en esta HU; #12413 los aplica).
/// </summary>
public sealed class UploadBrandLogoHandler(
    ITenantBrandingRepository repository,
    IBrandLogoStorage storage,
    IBrandAssetValidator validator)
{
    private readonly ITenantBrandingRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IBrandLogoStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    private readonly IBrandAssetValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));

    public async Task<UploadBrandLogoResult> HandleAsync(
        UploadBrandLogoCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var contentType = await ImageContentTypeSniffer.DetectAsync(command.Content, cancellationToken).ConfigureAwait(false);
        var (width, height) = await ImageDimensionsReader
            .ReadAsync(command.Content, contentType, cancellationToken)
            .ConfigureAwait(false);

        // El motor exige width_px/height_px > 0 (AC7); si el lector no reconoce el formato (fuera del
        // vocabulario permitido), 1x1 deja pasar el CHECK y el rechazo real de formato lo da #12413.
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);

        var errors = _validator.ValidateLogo(contentType, GetLength(command.Content), width, height);
        if (errors.Count > 0)
        {
            return UploadBrandLogoResult.Invalid(errors);
        }

        var stored = await _storage
            .SaveAsync(command.TenantId, command.Filename, command.Content, cancellationToken)
            .ConfigureAwait(false);

        var newLogo = new NewBrandLogo(contentType, command.Filename, stored.StoragePath, stored.Sha256, (int)stored.SizeBytes, width, height);

        try
        {
            var logo = await _repository
                .AddLogoVersionAsync(command.TenantId, newLogo, command.ChangedBy, cancellationToken)
                .ConfigureAwait(false);
            return UploadBrandLogoResult.Success(logo);
        }
        catch (BrandingTenantNotMarcaBlancaException)
        {
            return UploadBrandLogoResult.TenantNotMarcaBlanca();
        }
    }

    private static long GetLength(Stream stream) => stream.CanSeek ? stream.Length : 0;
}
