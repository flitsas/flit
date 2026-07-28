namespace Flit.DataMigration.V1.Mapping;

/// <summary>
/// Normaliza NITs para poder cruzar la compañía de V1 (<c>company_registered</c>) con el
/// tenant de V2 (<c>identity.tenants.tax_id</c>).
/// <para>
/// HALLAZGO que motiva esta clase: ambos lados mezclan formatos. En V1 conviven
/// <c>890903938</c> y <c>8909039382</c> para la MISMA empresa (Bancolombia), y en V2 conviven
/// <c>811222333-8</c> (con dígito de verificación) y <c>9000000001</c> (sin guion). Comparar
/// como texto plano mandaría trámites al tenant equivocado — el peor error posible en una
/// migración multi-tenant.
/// </para>
/// <para>
/// Estrategia: no se elige un único "canónico" (sería adivinar). Se generan todas las claves
/// plausibles y el resolutor exige coincidencia ÚNICA; si dos tenants distintos matchean, el
/// trámite va a cuarentena en vez de asignarse a ciegas.
/// </para>
/// </summary>
public static class NitNormalizer
{
    /// <summary>Pesos oficiales del DIAN para el dígito de verificación, de derecha a izquierda.</summary>
    private static readonly int[] DvWeights =
        [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];

    /// <summary>
    /// Claves de búsqueda plausibles para un NIT. Devuelve el número sin separadores y,
    /// cuando se detecta que el último dígito ES un dígito de verificación válido, también
    /// la versión sin él.
    /// </summary>
    public static IReadOnlySet<string> Keys(string? nit)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(nit))
        {
            return keys;
        }

        // Si viene con guion explícito ("811222333-8"), la parte izquierda es el NIT base
        // y la derecha el DV: no hay ambigüedad que resolver.
        var separatorIndex = nit.IndexOf('-', StringComparison.Ordinal);
        if (separatorIndex > 0)
        {
            var explicitBase = Digits(nit[..separatorIndex]);
            if (explicitBase.Length > 0)
            {
                keys.Add(explicitBase);
            }
        }

        var digits = Digits(nit);
        if (digits.Length == 0)
        {
            return keys;
        }

        keys.Add(digits);

        // Sin guion no se sabe si el último dígito es DV. Si al validarlo cuadra, se acepta
        // TAMBIÉN la forma sin DV como clave — sin descartar la forma completa.
        if (digits.Length > 1)
        {
            var candidateBase = digits[..^1];
            if (digits[^1] - '0' == ComputeVerificationDigit(candidateBase))
            {
                keys.Add(candidateBase);
            }
        }

        return keys;
    }

    /// <summary>Dígito de verificación (DV) de un NIT según el algoritmo del DIAN.</summary>
    public static int ComputeVerificationDigit(string baseNit)
    {
        ArgumentNullException.ThrowIfNull(baseNit);

        var sum = 0;
        var position = 0;
        for (var i = baseNit.Length - 1; i >= 0 && position < DvWeights.Length; i--, position++)
        {
            if (!char.IsDigit(baseNit[i]))
            {
                continue;
            }

            sum += (baseNit[i] - '0') * DvWeights[position];
        }

        var remainder = sum % 11;
        return remainder is 0 or 1 ? remainder : 11 - remainder;
    }

    private static string Digits(string value) =>
        string.Concat(value.Where(char.IsDigit));
}
