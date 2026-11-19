using System.Globalization;
using System.Text;

namespace Flit.PersonaNatural.Api.Consultas;

/// <summary>
/// La única regla con la que el BFF decide si el organismo que reporta el RUNT (<c>transit_office_name</c>) es el organismo del
/// trámite, en el portal web y en el punto por igual. Equivale a <c>puerta-iniciar.ts</c> del portal: sin tildes ni símbolos,
/// en mayúsculas; coinciden si son iguales o si uno contiene al otro y el más corto tiene al menos <see cref="MinimoContencion"/>
/// caracteres. Es algo más estricta que el front en un punto: la contención es por palabras completas, así «SABANETA» no se
/// encuentra dentro de «SABANETAS», ni un nombre corto dentro de una palabra de otro.
/// </summary>
public static class CoincidenciaOrganismo
{
    public const int MinimoContencion = 6;

    public const string MensajeOrganismoDesconocido = "No podemos radicar el trámite porque la consulta no trajo el organismo de tránsito donde está matriculado este vehículo.";

    public const string CodigoNoCorresponde = "organismo_no_corresponde";

    private const string ConDiacritico = "ÀÁÂÃÄÅàáâãäåÈÉÊËèéêëÌÍÎÏìíîïÒÓÔÕÖòóôõöÙÚÛÜùúûüÑñÇç";
    private const string SinDiacritico = "AAAAAAaaaaaaEEEEeeeeIIIIiiiiOOOOOoooooUUUUuuuuNnCc";

    public static string Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return string.Empty;

        // Sin string.Normalize: el servicio corre con InvariantGlobalization (la imagen alpine no trae ICU) y ahí NO descompone las
        // tildes. Se pliegan a mano las letras con diacrítico y se ignoran las marcas combinadas de un texto ya descompuesto.
        var sb = new StringBuilder(valor.Length);
        var espacio = false;
        foreach (var original in valor)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(original) == UnicodeCategory.NonSpacingMark)
                continue;
            var c = original;
            var i = ConDiacritico.IndexOf(c, StringComparison.Ordinal);
            if (i >= 0)
                c = SinDiacritico[i];
            var u = char.ToUpperInvariant(c);
            if (u is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            {
                sb.Append(u);
                espacio = false;
            }
            else if (!espacio)
            {
                sb.Append(' ');
                espacio = true;
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// ¿El nombre del RUNT es alguno de los <paramref name="candidatos"/> (nombre, municipio o código del organismo)? Un nombre del
    /// RUNT vacío nunca coincide: quien llama decide qué hacer cuando el RUNT no trae organismo.
    /// </summary>
    public static bool Coincide(string? nombreRunt, params string?[] candidatos)
    {
        var objetivo = Normalizar(nombreRunt);
        if (objetivo.Length == 0)
            return false;

        foreach (var candidato in candidatos)
        {
            var otro = Normalizar(candidato);
            if (otro.Length == 0)
                continue;
            if (otro == objetivo)
                return true;

            var (corto, largo) = otro.Length < objetivo.Length ? (otro, objetivo) : (objetivo, otro);
            if (corto.Length >= MinimoContencion && $" {largo} ".Contains($" {corto} ", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Texto del organismo del RUNT tal cual, solo recortado y con los espacios colapsados.</summary>
    public static string Limpiar(string? organismoRunt) =>
        string.Join(' ', (organismoRunt ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Mismo texto del punto: dice dónde está registrada la placa y dónde se hace el trámite. Sin datos del propietario.</summary>
    public static string MensajeOtroOrganismo(string? organismoRunt, string organismoDelTramite)
    {
        var runt = Limpiar(organismoRunt);
        return runt.Length == 0
            ? "Este trámite se hace en el organismo que tiene el vehículo."
            : $"Esta placa está registrada en {runt}, no en {organismoDelTramite.Trim()}. Este trámite debe hacerse en {runt}.";
    }
}
