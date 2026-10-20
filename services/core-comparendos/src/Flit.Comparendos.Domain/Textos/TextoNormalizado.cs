using System.Text;

namespace Flit.Comparendos.Domain.Textos;

/// <summary>
/// Texto para comparar nombres de secretarías y estados de fuente: mayúsculas, sin tildes ni espacios dobles. Las
/// tildes se quitan con un mapa propio porque el servicio compila con InvariantGlobalization (sin ICU,
/// string.Normalize no descompone caracteres que no son ASCII).
/// </summary>
public static class TextoNormalizado
{
    private const string ConTilde = "ÁÀÄÂÃÉÈËÊÍÌÏÎÓÒÖÔÕÚÙÜÛÑÇáàäâãéèëêíìïîóòöôõúùüûñç";
    private const string SinTilde = "AAAAAEEEEIIIIOOOOOUUUUNCaaaaaeeeeiiiiooooouuuunc";

    public static string De(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        var sb = new StringBuilder(texto.Length);
        var espacio = false;
        foreach (var original in texto.Trim())
        {
            var i = ConTilde.IndexOf(original, StringComparison.Ordinal);
            var ch = char.ToUpperInvariant(i >= 0 ? SinTilde[i] : original);
            if (char.IsWhiteSpace(ch))
            {
                if (!espacio)
                    sb.Append(' ');
                espacio = true;
                continue;
            }

            espacio = false;
            sb.Append(ch);
        }

        return sb.ToString();
    }
}
