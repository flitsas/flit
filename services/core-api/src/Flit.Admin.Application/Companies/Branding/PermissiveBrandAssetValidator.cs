using Flit.Admin.Domain.Companies.Branding;

namespace Flit.Admin.Application.Companies.Branding;

/// <summary>
/// Implementación permisiva de <see cref="IBrandAssetValidator"/> (HU #12412): acepta cualquier
/// borrador y cualquier logotipo que ya haya pasado la firma binaria (<c>ImageContentTypeSniffer</c>).
/// Las reglas reales (longitud de nombre, formato hex, contraste WCAG, peso/dimensiones) las aporta
/// #12413 registrando su propia implementación en <c>AdminInfrastructureExtensions</c> — este tipo no
/// se borra, queda como respaldo fail-open documentado.
/// </summary>
public sealed class PermissiveBrandAssetValidator : IBrandAssetValidator
{
    public IReadOnlyList<BrandAssetValidationError> ValidateDraft(BrandingDraft draft) =>
        Array.Empty<BrandAssetValidationError>();

    public IReadOnlyList<BrandAssetValidationError> ValidateLogo(
        string contentType, long sizeBytes, int widthPx, int heightPx) =>
        Array.Empty<BrandAssetValidationError>();
}
