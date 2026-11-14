using System.Text;

namespace Flit.Diagnostico.Domain.Saneamiento;

/// <summary>Por qué un texto libre no es válido.</summary>
public enum ErrorTextoLibre
{
    /// <summary>Falta (siendo obligatorio) o su largo en puntos de código está fuera del rango.</summary>
    Largo,

    /// <summary>Empieza o termina con espacios o saltos de línea.</summary>
    Formato,

    /// <summary>Trae un carácter de control (salvo salto de línea, retorno o tabulador) o una marca de dirección bidireccional.</summary>
    CaracterInvalido,
}

/// <summary>
/// Textos libres del saneamiento que ve el cliente (plan §3.2, SP-13 con la decisión E3 del usuario): largo medido en puntos de
/// código (como el CHECK con <c>char_length</c>), sin espacios ni saltos al inicio o al final, sin caracteres de control ni marcas
/// bidireccionales (U+202A a U+202E, U+2066 a U+2069, U+200E, U+200F y U+061C). Los dígitos seguidos se aceptan (E3: el cliente ve
/// todo). Los códigos de error son fijos y nunca repiten el texto.
/// </summary>
/// <example>
/// <code>
/// TextoLibreSaneamiento.CodigoMotivo("corto");       // MOTIVO_REQUERIDO
/// TextoLibreSaneamiento.Validar("Embargo", 3, 200); // null
/// </code>
/// </example>
public static class TextoLibreSaneamiento
{
    public const string MotivoRequerido = "MOTIVO_REQUERIDO";
    public const string TextoInvalido = "TEXTO_INVALIDO";

    /// <summary>Largo mínimo del motivo de NO_PROCEDE, reapertura y anulación (§3.2).</summary>
    public const int MotivoMinimo = 10;

    /// <summary>Largo máximo del motivo.</summary>
    public const int MotivoMaximo = 500;

    /// <summary>Null si el texto es válido. Con <paramref name="minimo"/> 0 un texto null es válido (opcional).</summary>
    public static ErrorTextoLibre? Validar(string? texto, int minimo, int maximo)
    {
        if (texto is null)
            return minimo > 0 ? ErrorTextoLibre.Largo : null;
        if (texto.EnumerateRunes().Any(Prohibido))
            return ErrorTextoLibre.CaracterInvalido;
        if (texto.Length > 0 && (char.IsWhiteSpace(texto[0]) || char.IsWhiteSpace(texto[^1])))
            return ErrorTextoLibre.Formato;
        var largo = texto.EnumerateRunes().Count();
        return largo < minimo || largo > maximo ? ErrorTextoLibre.Largo : null;
    }

    /// <summary>
    /// Código del motivo obligatorio: <see cref="MotivoRequerido"/> si falta o su largo o su forma no sirven; <see cref="TextoInvalido"/>
    /// con caracteres prohibidos; null si es válido.
    /// </summary>
    public static string? CodigoMotivo(string? motivo) => Validar(motivo, MotivoMinimo, MotivoMaximo) switch
    {
        null => null,
        ErrorTextoLibre.CaracterInvalido => TextoInvalido,
        _ => MotivoRequerido,
    };

    private static bool Prohibido(Rune rune) =>
        rune.Value is not ('\n' or '\r' or '\t')
        && (Rune.IsControl(rune) || rune.Value is (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069) or 0x200E or 0x200F or 0x061C);
}
