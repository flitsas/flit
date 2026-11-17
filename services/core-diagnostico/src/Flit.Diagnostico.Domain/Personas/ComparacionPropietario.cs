using System.Text;

namespace Flit.Diagnostico.Domain.Personas;

/// <summary>
/// ¿Cambió el propietario? (HU #14582, contrato §1.4, delta D-3, SP-3): el propietario actual que declaró el cliente frente a los
/// propietarios que reporta el RUNT. Los tipos se homologan con <see cref="HomologacionTipoDocumento"/>; el número se normaliza
/// (solo letras y dígitos, mayúsculas; fuera del pasaporte, sin ceros a la izquierda y no comparable si queda alguna letra). Un
/// propietario coincide con el mismo canónico y el mismo número; en el NIT, también si el más largo tiene exactamente un dígito
/// más (el de verificación) y el más corto al menos 8. Resultado: null sin declarado, con declarado no homologable o sin
/// propietarios; false si alguno coincide; true si ninguno coincide y todos eran homologables; null si ninguno coincide y alguno
/// no lo era (conservador). D-3 (H-A1=b, H-A2=b, 2026-11-16): el LEASING no tiene trato especial (los locatarios no entran) y CC
/// y NIT no son equivalentes. Regla pura, en memoria y sin logs: nunca escribe ni devuelve los documentos.
/// </summary>
/// <example>
/// <code>
/// ComparacionPropietario.Comparar(("CC", "1020304050"), [("C", "1020304050")])  // false: coincide
/// ComparacionPropietario.Comparar(("NIT", "900123456"), [("N", "9001234567")])  // false: NIT con dígito de verificación
/// ComparacionPropietario.Comparar(("CC", "1020304050"), [("CC", "79000111")])    // true: hubo cambio de propietario
/// </code>
/// </example>
public static class ComparacionPropietario
{
    private const string Nit = "NIT";
    private const string Pasaporte = "PAS";
    private const int LargoMinimoNitSinDv = 8;

    /// <summary>true si el declarado no coincide con ningún propietario del RUNT; false si coincide; null si no se puede decidir.</summary>
    /// <param name="declarado">Tipo y número del propietario actual declarado (en claro, solo en memoria); null si no hubo.</param>
    /// <param name="propietariosRunt">Tipo y número de cada propietario que trae el RUNT (en claro, solo en memoria).</param>
    public static bool? Comparar((string Tipo, string Numero)? declarado, IReadOnlyList<(string Tipo, string Numero)> propietariosRunt)
    {
        ArgumentNullException.ThrowIfNull(propietariosRunt);
        if (declarado is not { } d || Comparable(d.Tipo, d.Numero) is not { } esperado || propietariosRunt.Count == 0)
            return null;

        var todosHomologables = true;
        foreach (var (tipo, numero) in propietariosRunt)
        {
            if (Comparable(tipo, numero) is not { } propietario)
            {
                todosHomologables = false;
                continue;
            }

            if (Coinciden(esperado, propietario))
                return false;
        }

        return todosHomologables ? true : null;
    }

    private static bool Coinciden((string Tipo, string Numero) a, (string Tipo, string Numero) b)
    {
        if (a.Tipo != b.Tipo)
            return false;
        if (a.Numero == b.Numero)
            return true;
        if (a.Tipo != Nit)
            return false;
        var (corto, largo) = a.Numero.Length < b.Numero.Length ? (a.Numero, b.Numero) : (b.Numero, a.Numero);
        return corto.Length >= LargoMinimoNitSinDv && largo.Length == corto.Length + 1 && largo.StartsWith(corto, StringComparison.Ordinal);
    }

    /// <summary>Tipo canónico y número normalizado; null si el tipo no se homologa o el número no es comparable.</summary>
    private static (string Tipo, string Numero)? Comparable(string? tipo, string? numero)
    {
        if (HomologacionTipoDocumento.Canonico(tipo) is not { } canonico || numero is null)
            return null;

        var limpio = new StringBuilder(numero.Length);
        foreach (var c in numero)
        {
            if (char.IsAsciiLetterOrDigit(c))
                limpio.Append(char.ToUpperInvariant(c));
        }

        var normalizado = limpio.ToString();
        if (canonico != Pasaporte)
        {
            if (!normalizado.All(char.IsAsciiDigit))
                return null;
            normalizado = normalizado.TrimStart('0');
        }

        return normalizado.Length == 0 ? null : (canonico, normalizado);
    }
}
