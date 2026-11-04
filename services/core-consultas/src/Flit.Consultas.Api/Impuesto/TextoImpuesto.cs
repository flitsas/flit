using System.Globalization;
using System.Text;

namespace Flit.Consultas.Api.Impuesto;

/// <summary>Mayúsculas y tildes fuera, para comparar frases del servicio sin copiarlas al resultado.</summary>
internal static class TextoImpuesto
{
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return "";
        var formado = texto.Normalize(NormalizationForm.FormD);
        var plano = new StringBuilder(formado.Length);
        foreach (var caracter in formado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caracter) != UnicodeCategory.NonSpacingMark)
                plano.Append(char.ToLowerInvariant(caracter));
        }

        return plano.ToString();
    }
}
