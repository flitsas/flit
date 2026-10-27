using System.Text.RegularExpressions;

namespace Flit.PersonaNatural.Api.Consultas;

/// <summary>
/// Formato de lo que el ciudadano escribe, validado ANTES de llamar a core-consultas: una consulta con un dato mal formado
/// costaría una llamada al RUNT que no puede devolver nada. Cada método devuelve la forma canónica o <c>null</c> si el dato
/// no es válido (así quien llama no confunde «vacío» con «válido»).
/// </summary>
public static partial class EntradaConsulta
{
    /// <summary>Tipos de documento de una persona natural que core-consultas sabe pasar al RUNT.</summary>
    public static readonly IReadOnlyList<string> TiposDocumento = ["CC", "CE", "TI", "PAS", "PPT"];

    /// <summary>
    /// Placa en mayúsculas y sin separadores. Acepta carro y moto (<c>ABC123</c>, <c>ABC12D</c>) y remolque o semirremolque
    /// (<c>R12345</c>, <c>S12345</c>); lo demás no es una placa que el RUNT atienda.
    /// </summary>
    public static string? Placa(string? placa)
    {
        var canonica = Canonica(placa);
        return canonica is not null && PlacaRegex().IsMatch(canonica) ? canonica : null;
    }

    /// <summary>VIN: 17 letras o números sin I, O ni Q (así lo define la norma ISO 3779).</summary>
    public static string? Vin(string? vin)
    {
        var canonico = Canonica(vin);
        return canonico is not null && VinRegex().IsMatch(canonico) ? canonico : null;
    }

    /// <summary>
    /// Documento sin puntos, comas ni espacios. Cédulas y tarjetas: solo dígitos (5 a 12); pasaporte y PPT: letras y
    /// dígitos (4 a 16). El tipo se acepta en cualquier capitalización.
    /// </summary>
    public static DocumentoConsulta? Documento(string? tipo, string? numero)
    {
        var codigo = (tipo ?? string.Empty).Trim().ToUpperInvariant();
        if (!TiposDocumento.Contains(codigo) || string.IsNullOrWhiteSpace(numero))
            return null;

        var limpio = new string(numero.Where(c => c is not ('.' or ',' or ' ' or '-')).Select(char.ToUpperInvariant).ToArray());
        var valido = codigo is "PAS" or "PPT" ? AlfanumericoRegex().IsMatch(limpio) : NumericoRegex().IsMatch(limpio);
        return valido ? new DocumentoConsulta(codigo, limpio) : null;
    }

    private static string? Canonica(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        return new string(valor.Where(c => c is not (' ' or '-')).Select(char.ToUpperInvariant).ToArray());
    }

    [GeneratedRegex("^([A-Z]{3}[0-9]{2}[A-Z0-9]|[RS][0-9]{5})$")]
    private static partial Regex PlacaRegex();

    [GeneratedRegex("^[A-HJ-NPR-Z0-9]{17}$")]
    private static partial Regex VinRegex();

    [GeneratedRegex("^[0-9]{5,12}$")]
    private static partial Regex NumericoRegex();

    [GeneratedRegex("^[A-Z0-9]{4,16}$")]
    private static partial Regex AlfanumericoRegex();
}
