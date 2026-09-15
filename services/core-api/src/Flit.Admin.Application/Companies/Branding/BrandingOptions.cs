namespace Flit.Admin.Application.Companies.Branding;

/// <summary>
/// Límites parametrizables de la identidad de marca (HU #12413 AC2/AC4 — "parametrizable en la
/// configuración de la plataforma"). Sección <c>Branding</c> de <c>appsettings.json</c>. Los
/// valores por defecto son los que fija la HU: 512 KB, 120x40 px mínimo, 2000x2000 px máximo,
/// contraste mínimo 4.5:1.
/// </summary>
public sealed class BrandingOptions
{
    public const string SectionName = "Branding";

    public BrandingLogoOptions Logo { get; set; } = new();

    /// <summary>Umbral mínimo de contraste WCAG (AC4). Por defecto 4.5:1.</summary>
    public double MinContrastRatio { get; set; } = 4.5;
}

public sealed class BrandingLogoOptions
{
    /// <summary>Peso máximo del logotipo en bytes (AC2). Por defecto 512 KB.</summary>
    public long MaxBytes { get; set; } = 524_288;

    /// <summary>Ancho mínimo en píxeles (AC2). Por defecto 120.</summary>
    public int MinWidth { get; set; } = 120;

    /// <summary>Alto mínimo en píxeles (AC2). Por defecto 40.</summary>
    public int MinHeight { get; set; } = 40;

    /// <summary>Ancho máximo en píxeles (AC2). Por defecto 2000.</summary>
    public int MaxWidth { get; set; } = 2000;

    /// <summary>Alto máximo en píxeles (AC2). Por defecto 2000.</summary>
    public int MaxHeight { get; set; } = 2000;

    /// <summary>Content-types aceptados por firma binaria (AC1). Por defecto PNG/JPEG/WebP.</summary>
    public IReadOnlyList<string> AllowedContentTypes { get; set; } =
        ["image/png", "image/jpeg", "image/webp"];
}
