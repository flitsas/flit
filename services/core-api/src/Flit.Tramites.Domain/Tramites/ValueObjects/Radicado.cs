using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Flit.Tramites.Domain.Enums;

namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Formato del radicado de un trámite (HU #12371, Feature #12150): <c>FT1-0000012</c>.
/// <para>
/// <b>Quién lo compone:</b> la base de datos, en un trigger <c>BEFORE INSERT</c> sobre
/// <c>tramites.procedure_instances</c> (DDL 108). Esta clase NO crea radicados: describe el
/// formato para que el resto del código lo <i>lea</i> —la búsqueda, las pruebas, los mensajes— y
/// fija en un solo sitio el mapa familia → prefijo, que el DDL replica y una prueba ata.
/// </para>
/// <para>
/// Las cuatro decisiones del PO (comentario en el Feature #12150 del 2026-09-10):
/// ancho mínimo <b>7</b>; contador <b>global</b> (la familia no cuenta aparte); <b>inmutable</b>
/// desde la creación; <b>FT1</b> = Matrículas, <b>FT2</b> = Traspaso, <b>FT3</b> = todo lo demás.
/// Una cuarta familia sería FT4: se añade aquí, en el <c>CASE</c> del trigger y en la prueba que
/// los compara, y en ningún otro sitio.
/// </para>
/// </summary>
public static partial class Radicado
{
    /// <summary>Ancho mínimo de la parte numérica. Es relleno, no tope: el 10.000.000 gana un dígito.</summary>
    public const int AnchoMinimo = 7;

    /// <summary>Patrón que impone el CHECK de la base: prefijo de familia, guion, al menos 7 dígitos.</summary>
    public const string PatronSql = "^FT[1-9]-[0-9]{7,}$";

    /// <summary>Prefijo de familia: <c>FT1</c>, <c>FT2</c>, <c>FT3</c>.</summary>
    public static string Prefijo(ProcedureFamily familia) => familia switch
    {
        ProcedureFamily.Matriculas => "FT1",
        ProcedureFamily.Traspaso => "FT2",
        ProcedureFamily.Otros => "FT3",
        _ => throw new ArgumentOutOfRangeException(nameof(familia), familia, "Familia sin prefijo de radicado."),
    };

    /// <summary>
    /// Compone el radicado tal como lo escribe el trigger: <c>FT2-0000015</c>. Sirve para leer lo que
    /// la base va a producir (pruebas, búsqueda con prefijo), no para asignarlo.
    /// </summary>
    public static string Componer(ProcedureFamily familia, long consecutivo)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consecutivo);
        return $"{Prefijo(familia)}-{consecutivo.ToString(CultureInfo.InvariantCulture).PadLeft(AnchoMinimo, '0')}";
    }

    /// <summary>
    /// Lo que un usuario escribió leído como radicado. <c>12</c>, <c>0000012</c>, <c>FT1-0000012</c>,
    /// <c>ft1 12</c> y <c>FT1.0000012</c> se leen todos como el consecutivo 12; los cuatro últimos
    /// además fijan el prefijo <c>FT1</c>.
    /// </summary>
    /// <param name="consecutivo">La parte numérica, sin ceros a la izquierda.</param>
    /// <param name="prefijo"><c>FT1</c>/<c>FT2</c>/… si el usuario lo escribió; <c>null</c> si solo puso dígitos.</param>
    public readonly record struct Lectura(long Consecutivo, string? Prefijo)
    {
        /// <summary>
        /// Forma canónica sin guion, comparable con <c>reference_number</c> normalizado del mismo
        /// modo. Solo tiene sentido cuando hay prefijo: sin él, se busca por el consecutivo.
        /// </summary>
        public string? CanonicoSinGuion =>
            Prefijo is null ? null : $"{Prefijo}{Consecutivo.ToString(CultureInfo.InvariantCulture).PadLeft(AnchoMinimo, '0')}";
    }

    /// <summary>
    /// Intenta leer <paramref name="texto"/> como radicado. Tolera mayúsculas, espacios, puntos y
    /// guiones, y ceros a la izquierda. Devuelve <c>false</c> si no hay dígitos, si el número es
    /// cero o si no cabe en un <see cref="long"/>: en ese caso el texto no es un radicado y el que
    /// llama decide qué hacer con él (normalmente, buscarlo como texto en otros campos).
    /// </summary>
    public static bool TryLeer(string? texto, [NotNullWhen(true)] out Lectura? lectura)
    {
        lectura = null;
        if (string.IsNullOrWhiteSpace(texto))
            return false;

        var normalizado = texto.ToUpperInvariant()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal);

        var m = PatronLectura().Match(normalizado);
        if (!m.Success)
            return false;

        if (!long.TryParse(m.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n <= 0)
            return false;

        var prefijo = m.Groups["p"].Success ? m.Groups["p"].Value : null;
        lectura = new Lectura(n, prefijo);
        return true;
    }

    // El prefijo es opcional; los dígitos, no. Sobre el texto ya normalizado (sin guion ni espacios).
    [GeneratedRegex("^(?<p>FT[1-9])?(?<n>[0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex PatronLectura();
}
