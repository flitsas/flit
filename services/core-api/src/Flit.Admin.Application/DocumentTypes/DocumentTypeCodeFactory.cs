using System.Globalization;
using System.Text;

namespace Flit.Admin.Application.DocumentTypes;

/// <summary>
/// Genera el <c>code</c> del catálogo a partir del nombre visible. El usuario no elige
/// ni edita el código: es un identificador técnico único (máx. 50, letras/números/guiones).
/// </summary>
public static class DocumentTypeCodeFactory
{
    public const string Fallback = "DOC";

    /// <summary>Slug en mayúsculas a partir del nombre (acentos normalizados).</summary>
    public static string FromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fallback;
        }

        var folded = name.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(folded.Length);
        var pendingHyphen = false;

        foreach (var ch in folded)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var ascii = ToAsciiLetterOrDigit(ch);
            if (ascii is { } kept)
            {
                if (pendingHyphen && sb.Length > 0)
                {
                    sb.Append('-');
                }

                pendingHyphen = false;
                sb.Append(kept);
            }
            else if (sb.Length > 0)
            {
                pendingHyphen = true;
            }
        }

        var code = sb.ToString();
        if (code.Length > DocumentTypeValidator.CodeMaxLength)
        {
            code = code[..DocumentTypeValidator.CodeMaxLength].TrimEnd('-');
        }

        return string.IsNullOrEmpty(code) ? Fallback : code;
    }

    /// <summary>Solo A–Z / 0–9 para cumplir el patrón del código (sin tildes ni ñ).</summary>
    private static char? ToAsciiLetterOrDigit(char ch)
    {
        var upper = char.ToUpperInvariant(ch);
        upper = upper switch
        {
            'Á' or 'À' or 'Ä' or 'Â' => 'A',
            'É' or 'È' or 'Ë' or 'Ê' => 'E',
            'Í' or 'Ì' or 'Ï' or 'Î' => 'I',
            'Ó' or 'Ò' or 'Ö' or 'Ô' => 'O',
            'Ú' or 'Ù' or 'Ü' or 'Û' => 'U',
            'Ñ' => 'N',
            'Ç' => 'C',
            _ => upper,
        };

        if (upper is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return upper;
        }

        return null;
    }

    /// <summary>
    /// Primera variante libre: base, luego <c>base-2</c>, <c>base-3</c>… respetando 50 caracteres.
    /// </summary>
    public static async Task<string> AllocateUniqueAsync(
        string name,
        Func<string, CancellationToken, Task<bool>> codeExists,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codeExists);

        var stem = FromName(name);
        var candidate = stem;
        var n = 2;
        while (await codeExists(candidate, cancellationToken).ConfigureAwait(false))
        {
            var suffix = "-" + n.ToString(CultureInfo.InvariantCulture);
            var maxStem = DocumentTypeValidator.CodeMaxLength - suffix.Length;
            if (maxStem < 1)
            {
                candidate = Fallback + suffix;
            }
            else
            {
                var trimmed = stem.Length <= maxStem ? stem : stem[..maxStem].TrimEnd('-');
                candidate = trimmed + suffix;
            }

            n++;
            if (n > 10_000)
            {
                throw new InvalidOperationException("No se pudo asignar un código único al tipo de documento.");
            }
        }

        return candidate;
    }
}
