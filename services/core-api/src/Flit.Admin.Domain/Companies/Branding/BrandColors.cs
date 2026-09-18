namespace Flit.Admin.Domain.Companies.Branding;

/// <summary>
/// Paleta de la marca (HU #12412, ADR-0060 D1). Tres colores hex (<c>#RRGGBB</c>, mayúsculas tras
/// normalizar). El formato y el contraste WCAG los valida <c>IBrandAssetValidator</c> (permisivo en
/// esta HU; #12413 sustituye la implementación).
/// </summary>
public sealed record BrandColors(string Primary, string Secondary, string OnPrimary);
