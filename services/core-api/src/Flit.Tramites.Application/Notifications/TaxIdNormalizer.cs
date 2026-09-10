namespace Flit.Tramites.Application.Notifications;

/// <summary>
/// HU #11486 (ADR-0046) — normaliza NIT colombiano para comparación de marca Renting:
/// solo dígitos de la base, descartando separadores y dígito de verificación.
/// </summary>
public static class TaxIdNormalizer
{
    /// <summary>
    /// Extrae la base numérica del NIT (máx. 9 dígitos). Formatos como
    /// <c>811011779-1</c> o <c>811.011.779</c> deben coincidir con <c>811011779</c>.
    /// </summary>
    public static string NormalizeBase(string? taxId)
    {
        if (string.IsNullOrWhiteSpace(taxId))
            return string.Empty;

        var segment = taxId.Trim();
        var hyphen = segment.IndexOf('-');
        if (hyphen >= 0)
            segment = segment[..hyphen];

        var digits = new string(segment.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return string.Empty;

        return digits.Length > 9 ? digits[..9] : digits;
    }
}
