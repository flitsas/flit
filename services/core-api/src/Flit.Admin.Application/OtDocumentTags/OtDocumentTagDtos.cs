using System.Text.Json.Serialization;
using Flit.Admin.Domain.OtDocumentTags;

namespace Flit.Admin.Application.OtDocumentTags;

public sealed class CreateOtDocumentTagRequest
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = "#162744";
}

public sealed class OtDocumentTagResponse
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Color { get; init; } = "#162744";
}

internal static class OtDocumentTagMapper
{
    public static OtDocumentTagResponse ToResponse(OtDocumentTag tag) => new()
    {
        Id = tag.Id,
        Code = tag.Code,
        Name = tag.Name,
        Color = tag.Color,
    };
}

internal static class OtDocumentTagColorValidator
{
    private static readonly System.Text.RegularExpressions.Regex HexColorRegex = new(
        @"^#[0-9A-Fa-f]{6}$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant
            | System.Text.RegularExpressions.RegexOptions.Compiled);

    public static bool IsValid(string? color) =>
        !string.IsNullOrEmpty(color) && color.Length == 7 && HexColorRegex.IsMatch(color);
}
