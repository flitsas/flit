using System.Text.RegularExpressions;

namespace Flit.Admin.Application.Companies.Whitelist;

/// <summary>
/// Validación de formato de correo (AC5), RFC 5321 simplificada: exactamente una
/// arroba, sin espacios y con dominio con punto. Rechaza valores como
/// <c>notanemail</c>. Usa <see cref="GeneratedRegexAttribute"/> para mantener el
/// ensamblado AOT-compatible (regex compilada en tiempo de build, sin reflexión).
/// </summary>
internal static partial class WhitelistEmailValidator
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    public static bool IsValid(string? email) =>
        !string.IsNullOrWhiteSpace(email) && EmailPattern().IsMatch(email.Trim());
}
