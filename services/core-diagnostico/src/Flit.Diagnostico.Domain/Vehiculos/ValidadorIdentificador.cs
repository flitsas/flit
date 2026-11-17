using System.Text.RegularExpressions;

namespace Flit.Diagnostico.Domain.Vehiculos;

/// <summary>
/// Fuente de verdad de la normalización y la clasificación de identificadores de vehículo (HU #13740, F02-H2, decisión J1).
/// Replica la regla de Trámites (<c>frontend/lib/validation/fieldRules.ts</c>, decisión del usuario 2026-10-21):
/// primero se prueba como VIN con <c>sanitizeVin</c> (mayúsculas, solo A-H, J-N, P, R-Z y 0-9, corte a 17) y
/// <c>VIN_PATTERN</c>; si no casa, como placa con <c>sanitizePlate</c> (mayúsculas, solo A-Z y 0-9, corte a 8) y las
/// ramas de <c>PLATE_PATTERN</c>. El VIN va primero porque el corte a 8 de la placa convertiría en maquinaria a un VIN
/// que empiece por dos letras y seis dígitos. El normalizado alimenta <c>diagnostico.vehiculos.identificador_norm</c>.
/// Casos compartidos con el frontend: <c>contracts/fixtures/validacion/identificadores-vehiculo.json</c>.
/// </summary>
/// <remarks>
/// El corte a 8 y a 17 es la máscara de captura de Trámites: una entrada demasiado larga puede quedar válida
/// (<c>AB1234567</c> → <c>AB123456</c>, maquinaria). Es la regla vigente por decisión del usuario.
/// </remarks>
/// <example>
/// <code>
/// ValidadorIdentificador.Validar(" mc-029.554 ") // → ("MC029554", TipoIdentificador.PlacaMaquinaria)
/// ValidadorIdentificador.Validar("ABC-123")      // → ("ABC123", TipoIdentificador.PlacaCarro)
/// ValidadorIdentificador.Validar("ABC123456")    // → ("ABC123456", null): ni placa ni VIN
/// </code>
/// </example>
public static partial class ValidadorIdentificador
{
    private const int LargoPlaca = 8;
    private const int LargoVin = 17;

    /// <summary>
    /// Normalizado de <see cref="Validar"/>: el de <c>sanitizeVin</c> si es VIN, el de <c>sanitizePlate</c> si es placa y,
    /// si es inválido, el valor en mayúsculas con solo A-Z y 0-9, sin cortar. <see langword="null"/> da cadena vacía.
    /// </summary>
    public static string Normalizar(string? identificador) => Validar(identificador).Normalizado;

    /// <summary>Normaliza y clasifica. Un identificador que no es placa ni VIN queda con <c>Tipo</c> nulo (INVALIDO).</summary>
    public static ResultadoValidacion Validar(string? identificador)
    {
        if (string.IsNullOrEmpty(identificador))
            return new ResultadoValidacion(string.Empty, null);

        var mayusculas = identificador.ToUpperInvariant();

        var vin = Cortar(FueraDeVin().Replace(mayusculas, string.Empty), LargoVin);
        if (Vin().IsMatch(vin))
            return new ResultadoValidacion(vin, TipoIdentificador.Vin);

        var alfanumerico = FueraDePlaca().Replace(mayusculas, string.Empty);
        var placa = Cortar(alfanumerico, LargoPlaca);
        return ClasificarPlaca(placa) is { } tipo
            ? new ResultadoValidacion(placa, tipo)
            : new ResultadoValidacion(alfanumerico, null);
    }

    private static string Cortar(string valor, int largo) => valor.Length > largo ? valor[..largo] : valor;

    private static TipoIdentificador? ClasificarPlaca(string placa)
    {
        if (PlacaCarro().IsMatch(placa))
            return TipoIdentificador.PlacaCarro;
        if (PlacaMoto().IsMatch(placa))
            return TipoIdentificador.PlacaMoto;
        if (PlacaRemolque().IsMatch(placa))
            return TipoIdentificador.PlacaRemolque;
        if (PlacaMaquinaria().IsMatch(placa))
            return TipoIdentificador.PlacaMaquinaria;
        return null;
    }

    // sanitizePlate: /[^A-Z0-9]/g sobre el valor ya en mayúsculas.
    [GeneratedRegex(@"[^A-Z0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex FueraDePlaca();

    // sanitizeVin: /[^A-HJ-NPR-Z0-9]/g sobre el valor ya en mayúsculas (quita también I, O y Q).
    [GeneratedRegex(@"[^A-HJ-NPR-Z0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex FueraDeVin();

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
