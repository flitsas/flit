using System.Text;

namespace Flit.Diagnostico.Domain.Catalogos;

/// <summary>
/// Forma normalizada de un nombre para emparejar textos de distintas fuentes (HU #13733): mayúsculas invariantes, sin tildes y
/// con un solo espacio entre palabras. Las tildes se quitan con una tabla propia (vocales con tilde, diéresis o acento grave, y
/// la Ñ): <c>string.Normalize</c> no las descompuso en el entorno de pruebas. La usan la réplica de organismos
/// (<c>organismos.nombre_norm</c>), la derivación organismo → departamento con el <c>transit_office_name</c> del RUNT y la
/// regla del servicio público.
/// </summary>
/// <example>
/// <code>
/// NombreNormalizado.Normalizar("  Secretaría de  Tránsito ") // "SECRETARIA DE TRANSITO"
/// </code>
/// </example>
public static class NombreNormalizado
{
    private const string ConTilde = "ÁÀÄÂÉÈËÊÍÌÏÎÓÒÖÔÚÙÜÛÑáàäâéèëêíìïîóòöôúùüûñ";
    private const string SinTilde = "AAAAEEEEIIIIOOOOUUUUNAAAAEEEEIIIIOOOOUUUUN";

    /// <summary>El nombre normalizado; cadena vacía si es nulo o solo espacios.</summary>
    public static string Normalizar(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            return string.Empty;
        var sb = new StringBuilder(nombre.Length);
        foreach (var c in nombre)
        {
            var i = ConTilde.IndexOf(c, StringComparison.Ordinal);
            sb.Append(i >= 0 ? SinTilde[i] : char.ToUpperInvariant(c));
        }

        return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
