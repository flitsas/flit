namespace Flit.Tramites.Domain.Identity;

/// <summary>
/// Normalización canónica del par (tipo, número) de documento de identidad (HU #11262, D8).
/// Regla única: <c>Trim</c> + mayúsculas invariantes. Nada más (sin quitar puntos, guiones ni
/// ceros a la izquierda; sin alias NIT/N ni CC/C — D11 fuera de alcance).
/// <para>
/// Punto de entrada previsto para el resolver de identidad y la vista agrupada por persona.
/// En esta HU aún no se cablean los consumidores existentes (AC3): el gate de radicación y las
/// cuatro normalizaciones históricas siguen intactas.
/// </para>
/// </summary>
public static class DocumentCanonicalNormalization
{
    /// <summary>Normaliza una parte del documento (tipo o número): Trim + ToUpperInvariant.</summary>
    public static string NormalizePart(string? value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Normaliza el par (tipo, número) con la regla canónica.</summary>
    public static (string DocumentType, string DocumentNumber) Normalize(
        string? documentType,
        string? documentNumber) =>
        (NormalizePart(documentType), NormalizePart(documentNumber));

    /// <summary>
    /// Clave de identidad canónica por persona dentro del tenant:
    /// <c>{tenant:N}|{TIPO}|{NUMERO}</c> tras <see cref="Normalize"/>.
    /// Misma forma que <see cref="Entities.BiometricRules.IdentidadKey"/>, expuesta aquí como
    /// API canónica nombrada para futuros consumidores (vista agrupada, precedencia de envío).
    /// </summary>
    public static string IdentidadKey(Guid tenantId, string? documentType, string? documentNumber)
    {
        var (tipo, numero) = Normalize(documentType, documentNumber);
        return $"{tenantId:N}|{tipo}|{numero}";
    }

    /// <summary>
    /// Empate exacto (regla histórica de igualdad SQL): tipo y número iguales sin normalizar.
    /// Útil para la medición D9 (pares que solo empatan tras Trim+Upper).
    /// </summary>
    public static bool MatchesExact(
        string? leftType,
        string? leftNumber,
        string? rightType,
        string? rightNumber) =>
        string.Equals(leftType ?? string.Empty, rightType ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(leftNumber ?? string.Empty, rightNumber ?? string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Empate canónico: ambas partes coinciden tras <see cref="Normalize"/>.
    /// </summary>
    public static bool MatchesCanonical(
        string? leftType,
        string? leftNumber,
        string? rightType,
        string? rightNumber)
    {
        var left = Normalize(leftType, leftNumber);
        var right = Normalize(rightType, rightNumber);
        return left.DocumentType == right.DocumentType && left.DocumentNumber == right.DocumentNumber;
    }
}
