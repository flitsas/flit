using System.Text.RegularExpressions;

namespace Flit.Admin.Domain.Companies.Branding;

/// <summary>
/// Reglas puras de formato de la identidad de marca (HU #12413 AC3/AC5): nombre de plataforma y
/// color hexadecimal. Sin dependencias de infraestructura — <c>BrandAssetValidator</c>
/// (Application) las orquesta con los códigos de <see cref="BrandingErrors"/>.
/// </summary>
public static partial class BrandingValidation
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 40;

    [GeneratedRegex(@"^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColorRegex();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex MarkupRegex();

    /// <summary>
    /// Recorta espacios en los extremos (AC5). No valida longitud ni markup — eso lo hacen
    /// <see cref="IsNameLengthValid"/> y <see cref="HasMarkup"/> sobre el valor YA recortado.
    /// </summary>
    public static string NormalizeName(string? name) => name?.Trim() ?? string.Empty;

    /// <summary>Longitud entre <see cref="MinNameLength"/> y <see cref="MaxNameLength"/> (AC5), tras recortar.</summary>
    public static bool IsNameLengthValid(string trimmedName) =>
        trimmedName.Length is >= MinNameLength and <= MaxNameLength;

    /// <summary>`true` si el nombre (ya recortado) contiene una etiqueta tipo <c>&lt;...&gt;</c> (AC5).</summary>
    public static bool HasMarkup(string trimmedName) => MarkupRegex().IsMatch(trimmedName);

    /// <summary>`true` si el valor cumple <c>^#[0-9A-Fa-f]{6}$</c> (AC3), sin recortar espacios (los espacios ya son inválidos).</summary>
    public static bool IsValidHexColor(string? value) => value is not null && HexColorRegex().IsMatch(value);

    /// <summary>Normaliza un hex válido a mayúsculas (AC3). No valida — llamar tras <see cref="IsValidHexColor"/>.</summary>
    public static string NormalizeHexColor(string value) => value.ToUpperInvariant();
}
