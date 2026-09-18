using Flit.Admin.Domain.Companies.Branding;

namespace Flit.Admin.Application.Companies.Branding;

/// <summary>
/// Implementación real de <see cref="IBrandAssetValidator"/> (HU #12413 AC1-AC6): formato de
/// nombre, formato de colores + normalización a mayúsculas, contraste WCAG mínimo, formato/peso/
/// dimensiones del logotipo. Sustituye a <see cref="PermissiveBrandAssetValidator"/> en DI
/// (<c>AdminInfrastructureExtensions</c>) sin tocar los handlers que la consumen.
/// <para>
/// Pares de contraste evaluados (AC4 — "entre el texto y el color principal, o entre el texto y el
/// fondo"): <c>onPrimary/primary</c> y <c>onPrimary/secondary</c> — <c>onPrimary</c> es el color de
/// texto sobre el color principal (contrato §3), y <c>secondary</c> se usa como color de fondo
/// alterno en la UI de marca (mismos dos pares que <c>frontend/lib/brand</c> mostrará, #12429).
/// </para>
/// </summary>
public sealed class BrandAssetValidator(BrandingOptions options) : IBrandAssetValidator
{
    private readonly BrandingOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public IReadOnlyList<BrandAssetValidationError> ValidateDraft(BrandingDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var opts = _options;
        var errors = new List<BrandAssetValidationError>();

        if (draft.PlatformName is not null)
        {
            ValidateName(draft.PlatformName, errors);
        }

        if (draft.Colors is not null)
        {
            ValidateColors(draft.Colors, opts, errors);
        }

        return errors;
    }

    public IReadOnlyList<BrandAssetValidationError> ValidateLogo(
        string contentType, long sizeBytes, int widthPx, int heightPx)
    {
        var opts = _options.Logo;
        var errors = new List<BrandAssetValidationError>();

        if (!opts.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(new BrandAssetValidationError(BrandingErrors.LogoFormat, "logo"));
            // Formato inválido: dimensiones/peso no se pueden interpretar de forma fiable — no
            // reportar errores adicionales derivados de una lectura que probablemente sea basura.
            return errors;
        }

        if (sizeBytes > opts.MaxBytes)
        {
            errors.Add(new BrandAssetValidationError(
                BrandingErrors.LogoTooLarge,
                "logo",
                new Dictionary<string, object?> { ["maxBytes"] = opts.MaxBytes }));
        }

        if (widthPx < opts.MinWidth || heightPx < opts.MinHeight
            || widthPx > opts.MaxWidth || heightPx > opts.MaxHeight)
        {
            errors.Add(new BrandAssetValidationError(
                BrandingErrors.LogoDimensions,
                "logo",
                new Dictionary<string, object?>
                {
                    ["min"] = new { width = opts.MinWidth, height = opts.MinHeight },
                    ["max"] = new { width = opts.MaxWidth, height = opts.MaxHeight },
                    ["actual"] = new { width = widthPx, height = heightPx },
                }));
        }

        return errors;
    }

    private static void ValidateName(string platformName, List<BrandAssetValidationError> errors)
    {
        var trimmed = BrandingValidation.NormalizeName(platformName);

        if (!BrandingValidation.IsNameLengthValid(trimmed))
        {
            errors.Add(new BrandAssetValidationError(BrandingErrors.NameLength, "platformName"));
            return;
        }

        if (BrandingValidation.HasMarkup(trimmed))
        {
            errors.Add(new BrandAssetValidationError(BrandingErrors.NameMarkup, "platformName"));
        }
    }

    private static void ValidateColors(BrandColors colors, BrandingOptions opts, List<BrandAssetValidationError> errors)
    {
        var formatOk = true;

        formatOk &= ValidateHex(colors.Primary, "colors.primary", errors);
        formatOk &= ValidateHex(colors.Secondary, "colors.secondary", errors);
        formatOk &= ValidateHex(colors.OnPrimary, "colors.onPrimary", errors);

        if (!formatOk)
        {
            // Contraste no se puede calcular sobre un hex inválido — el error de formato ya es
            // suficiente para bloquear (AC3); evita un segundo error confuso sobre el mismo campo.
            return;
        }

        ValidateContrastPair(colors.OnPrimary, colors.Primary, "onPrimary/primary", opts.MinContrastRatio, errors);
        ValidateContrastPair(colors.OnPrimary, colors.Secondary, "onPrimary/secondary", opts.MinContrastRatio, errors);
    }

    private static bool ValidateHex(string value, string field, List<BrandAssetValidationError> errors)
    {
        if (BrandingValidation.IsValidHexColor(value))
        {
            return true;
        }

        errors.Add(new BrandAssetValidationError(BrandingErrors.ColorFormat, field));
        return false;
    }

    private static void ValidateContrastPair(
        string foreground, string background, string pairLabel, double minRatio, List<BrandAssetValidationError> errors)
    {
        var ratio = WcagContrast.RoundedContrastRatio(foreground, background);
        if (WcagContrast.MeetsMinimum(ratio, minRatio))
        {
            return;
        }

        errors.Add(new BrandAssetValidationError(
            BrandingErrors.ContrastTooLow,
            null,
            new Dictionary<string, object?>
            {
                ["pair"] = pairLabel,
                ["ratio"] = ratio,
                ["min"] = minRatio,
            }));
    }
}
