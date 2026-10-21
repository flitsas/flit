using System.Text;
using System.Text.RegularExpressions;

namespace Flit.Diagnostico.Domain.Vehiculos;

/// <summary>
/// Fuente de verdad de la normalización y la clasificación de identificadores de vehículo (HU #13740, F02-H2, decisión J1).
/// Normaliza a mayúsculas y quita todo espacio en blanco; luego clasifica con los mismos patrones que
/// <c>frontend/lib/validation/fieldRules.ts</c> (<c>PLATE_PATTERN</c> y <c>VIN_PATTERN</c>). El normalizado alimenta
/// <c>diagnostico.vehiculos.identificador_norm</c>. Casos compartidos con el frontend:
/// <c>contracts/fixtures/validacion/identificadores-vehiculo.json</c>.
/// </summary>
/// <example>
/// <code>
/// ValidadorIdentificador.Validar(" mc029554 ") // → ("MC029554", TipoIdentificador.PlacaMaquinaria)
/// ValidadorIdentificador.Validar("ABC123456")  // → ("ABC123456", null): ni placa ni VIN
/// </code>
/// </example>
public static partial class ValidadorIdentificador
{
    /// <summary>Mayúsculas (invariante) y sin ningún espacio en blanco; <see langword="null"/> da cadena vacía.</summary>
    public static string Normalizar(string? identificador)
    {
        if (string.IsNullOrEmpty(identificador))
            return string.Empty;

        var sb = new StringBuilder(identificador.Length);
        foreach (var c in identificador)
        {
            if (!char.IsWhiteSpace(c))
                sb.Append(char.ToUpperInvariant(c));
        }

        return sb.ToString();
    }

    /// <summary>Normaliza y clasifica. Un identificador que no es placa ni VIN queda con <c>Tipo</c> nulo (INVALIDO).</summary>
    public static ResultadoValidacion Validar(string? identificador)
    {
        var normalizado = Normalizar(identificador);
        return new ResultadoValidacion(normalizado, Clasificar(normalizado));
    }

    private static TipoIdentificador? Clasificar(string normalizado)
    {
        if (PlacaCarro().IsMatch(normalizado))
            return TipoIdentificador.PlacaCarro;
        if (PlacaMoto().IsMatch(normalizado))
            return TipoIdentificador.PlacaMoto;
        if (PlacaRemolque().IsMatch(normalizado))
            return TipoIdentificador.PlacaRemolque;
        if (PlacaMaquinaria().IsMatch(normalizado))
            return TipoIdentificador.PlacaMaquinaria;
        if (Vin().IsMatch(normalizado))
            return TipoIdentificador.Vin;
        return null;
    }

    // Las cuatro ramas de PLATE_PATTERN, por separado para saber cuál casó. Sin IgnoreCase: el valor ya va en mayúsculas.
    [GeneratedRegex(@"^[A-Z]{3}[0-9]{3}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PlacaCarro();

    [GeneratedRegex(@"^[A-Z]{3}[0-9]{2}[A-Z]?\z", RegexOptions.CultureInvariant)]
    private static partial Regex PlacaMoto();

    [GeneratedRegex(@"^[RS][0-9]{5,6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PlacaRemolque();

    [GeneratedRegex(@"^[A-Z]{2}[0-9]{6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PlacaMaquinaria();

    // VIN_PATTERN: 17 caracteres sin I, O ni Q. No se valida el dígito verificador.
    [GeneratedRegex(@"^[A-HJ-NPR-Z0-9]{17}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Vin();
}
