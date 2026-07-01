using System.Text.RegularExpressions;

namespace Flit.Admin.Application.Common;

/// <summary>
/// Patrones de caracteres permitidos por TIPO de campo, compartidos por los casos de uso
/// de administración (compañías, tipos de documento). Centraliza las reglas para que el
/// backend valide igual que el frontend y no haya deriva entre capas.
///
/// Regla general: se valida el CONJUNTO de caracteres permitidos (allow-list), no una
/// lista negra. Las longitudes máximas las imponen los handlers/validadores de cada campo.
/// </summary>
public static partial class TextFieldPatterns
{
    /// <summary>
    /// Nombres legibles (razón social, nombre de tipo de documento): letras Unicode
    /// (incluye tildes y ñ), dígitos, espacios y puntuación básica de nombres
    /// (<c>. , &amp; ( ) / ' ° -</c>). Bloquea especiales como <c>@ # $ % ^ * { } [ ] &lt; &gt; | \ ~ `</c>.
    /// </summary>
    [GeneratedRegex(@"^[\p{L}\p{N}\s.,&()/'°-]+$")]
    public static partial Regex Name();

    /// <summary>NIT / identificador tributario: solo dígitos, puntos y guiones (p.ej. <c>900.123.456-7</c>).</summary>
    [GeneratedRegex(@"^[0-9.\-]+$")]
    public static partial Regex TaxId();

    /// <summary>Código de tenant: alfanumérico con guion y guion bajo.</summary>
    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    public static partial Regex TenantCode();

    /// <summary>Texto libre seguro (descripciones): cualquier carácter salvo <c>&lt;</c> y <c>&gt;</c> (anti-XSS).</summary>
    [GeneratedRegex("^[^<>]*$")]
    public static partial Regex FreeTextNoAngleBrackets();

    /// <summary>
    /// Contiene al menos una letra o dígito. Evita valores de pura puntuación (p.ej. <c>'--.,/()</c>):
    /// un nombre/código "válido" en caracteres pero sin contenido real no debe aceptarse.
    /// </summary>
    [GeneratedRegex(@"[\p{L}\p{N}]")]
    public static partial Regex HasLetterOrDigit();

    /// <summary>Contiene al menos un dígito (para NIT: evita un NIT de solo puntos/guiones).</summary>
    [GeneratedRegex("[0-9]")]
    public static partial Regex HasDigit();

    /// <summary>Contiene al menos una letra (un nombre legible no puede ser solo dígitos/puntuación).</summary>
    [GeneratedRegex(@"\p{L}")]
    public static partial Regex HasLetter();

    /// <summary>Empieza con letra o dígito (no con puntuación: rechaza nombres tipo <c>°&amp;&amp;//()</c>).</summary>
    [GeneratedRegex(@"^[\p{L}\p{N}]")]
    public static partial Regex StartsWithLetterOrDigit();

    /// <summary>
    /// Detecta dos o más signos de puntuación seguidos (<c>&amp;&amp;</c>, <c>//</c>, <c>((</c>, <c>..</c>):
    /// patrón de "spam" de símbolos. El espacio NO cuenta (así <c>"Autos &amp; Más"</c> es válido).
    /// </summary>
    [GeneratedRegex(@"[.,&()/'°-]{2,}")]
    public static partial Regex ConsecutivePunctuation();

    /// <summary>
    /// Valida la "fuerza" de un nombre legible (razón social, nombre de tipo de documento): conjunto de
    /// caracteres permitido, empieza con alfanumérico, contiene al menos una letra y no tiene puntuación
    /// repetida seguida. Devuelve un mensaje (con <paramref name="fieldLabel"/>) o <c>null</c> si es válido.
    /// Asume el valor ya recortado (trim).
    /// </summary>
    public static string? ValidateReadableName(string value, string fieldLabel)
    {
        if (!Name().IsMatch(value))
            return $"{fieldLabel} solo permite letras, números, espacios y . , & ( ) / ' -";
        if (!StartsWithLetterOrDigit().IsMatch(value))
            return $"{fieldLabel} debe empezar con una letra o un número.";
        if (!HasLetter().IsMatch(value))
            return $"{fieldLabel} debe contener al menos una letra.";
        if (ConsecutivePunctuation().IsMatch(value))
            return $"{fieldLabel} no debe tener símbolos especiales repetidos seguidos (p.ej. && // (( ).";
        return null;
    }
}
