namespace Flit.Tramites.Domain.Certifications.Normalization;

/// <summary>
/// Normaliza números de certificado: póliza de SOAT (<c>numSoat</c>), certificado de RTM
/// (<c>numeCerti</c>) y matrícula mercantil.
/// </summary>
/// <remarks>
/// <b>Se tratan como texto, nunca como número.</b> <c>numSoat</c> del RUNT llega con 16 dígitos —por
/// encima de <c>int</c>— y hay proveedores que anteponen ceros que forman parte del número impreso en
/// la póliza. Convertir a entero perdería ambos.
///
/// <para>Se descartan los rellenos que significan "no hay dato" y que hoy se imprimen como si fueran
/// un número: ceros a secas, <c>N/A</c>, <c>NO APLICA</c>, guiones. Se devuelven como valor ausente
/// —celda en blanco, regla HU #10856— conservando el crudo.</para>
/// </remarks>
public static class CertificateNumberNormalizer
{
    public const int MaxLength = 60;

    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "N/A", "NA", "N.A.", "NO APLICA", "NO_APLICA", "NINGUNO", "SIN DATO", "SIN INFORMACION",
        "DESCONOCIDO", "NULL", "NONE", "-", "--", "---", ".",
    };

    public static CertifiedNumber Normalize(string? raw)
    {
        var text = EntityNameNormalizer.Collapse(raw);
        if (text is null)
            return new CertifiedNumber(null, raw);

        if (Placeholders.Contains(text) || IsAllZeros(text))
            return new CertifiedNumber(null, raw);

        if (text.Length > MaxLength)
            text = text[..MaxLength];

        return new CertifiedNumber(text, raw);
    }

    /// <summary>Sobrecarga para proveedores que ya entregan el número tipado (y que por eso pierden los ceros a la izquierda).</summary>
    public static CertifiedNumber Normalize(long? value) =>
        value is null or 0 ? CertifiedNumber.Empty : Normalize(value.Value.ToString());

    private static bool IsAllZeros(string text)
    {
        var hasDigit = false;
        foreach (var ch in text)
        {
            if (ch == '0')
            {
                hasDigit = true;
                continue;
            }

            return false;
        }

        return hasDigit;
    }
}
