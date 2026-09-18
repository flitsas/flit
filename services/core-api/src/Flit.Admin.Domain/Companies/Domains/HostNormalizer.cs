using System.Buffers;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Flit.Admin.Domain.Companies.Domains;

/// <summary>
/// Normaliza y valida un host de dominio de red (HU #12416 AC1) SIN tocar infraestructura: recorta,
/// convierte IDN a punycode (<see cref="IdnMapping.GetAscii(string)"/>, comprobado bajo
/// <c>InvariantGlobalization=true</c> — Directory.Build.props — porque la conversión punycode es
/// autocontenida y no depende de ICU) y valida contra la MISMA expresión RFC 1123 del CHECK de BD
/// (<c>ck_tenant_domains_host_format</c>, DDL 116): etiquetas alfanuméricas con guiones internos
/// (1-63 caracteres), al menos dos etiquetas, TLD alfabético o punycode (<c>xn--</c>). Un dominio
/// internacionalizado mal formado (mezcla de escrituras, etiqueta vacía por puntos consecutivos, etc.)
/// hace que <c>GetAscii</c> lance <see cref="ArgumentException"/>, que aquí se traduce a
/// <see cref="DomainErrors.HostInvalid"/> — un unicode bien formado (p. ej. <c>españa.com</c>) SÍ se
/// acepta, convertido a su punycode (<c>xn--espaa-rta.com</c>).
/// </summary>
public static class HostNormalizer
{
    private const int MinLength = 4;
    private const int MaxLength = 253;

    private static readonly IdnMapping Idn = new();

    // Misma expresión que ck_tenant_domains_host_format (DDL 116) — no divergir sin actualizar ambas.
    private static readonly Regex Rfc1123 = new(
        @"^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+([a-z]{2,63}|xn--[a-z0-9-]{1,59})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly SearchValues<char> ForbiddenChars = SearchValues.Create("/?#:* \t\\");

    /// <summary>
    /// <c>true</c> y <paramref name="normalized"/> con el host listo para persistir; <c>false</c> y
    /// <paramref name="errorCode"/> (<see cref="DomainErrors.HostInvalid"/>) si no es un host de
    /// dominio válido. NO evalúa reservados (ver <see cref="ReservedHosts"/>).
    /// </summary>
    public static bool TryNormalize(string? rawHost, out string normalized, out string? errorCode)
    {
        normalized = string.Empty;
        errorCode = null;

        if (string.IsNullOrWhiteSpace(rawHost))
        {
            errorCode = DomainErrors.HostInvalid;
            return false;
        }

        var candidate = rawHost.Trim();

        // Esquema, ruta, puerto, comodín y espacios se rechazan ANTES de IdnMapping: sin este corte,
        // GetAscii podría lanzar por motivos distintos al enunciado (o, peor, procesar solo el primer
        // segmento) y el código de error dejaría de ser identificable como formato de host.
        if (candidate.Contains("://", StringComparison.Ordinal) || candidate.IndexOfAny(ForbiddenChars) >= 0)
        {
            errorCode = DomainErrors.HostInvalid;
            return false;
        }

        string ascii;
        try
        {
            ascii = Idn.GetAscii(candidate).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            errorCode = DomainErrors.HostInvalid;
            return false;
        }

        if (ascii.Length is < MinLength or > MaxLength
            || !Rfc1123.IsMatch(ascii)
            || string.Equals(ascii, "localhost", StringComparison.Ordinal))
        {
            errorCode = DomainErrors.HostInvalid;
            return false;
        }

        normalized = ascii;
        return true;
    }
}
