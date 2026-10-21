using System.Text.RegularExpressions;
using Flit.Diagnostico.Domain.Personas;

namespace Flit.Diagnostico.Domain.Vehiculos;

/// <summary>Columna de la fila capturada a la que apunta un error de <see cref="ValidadorFilaVehiculo"/>.</summary>
public enum CampoFilaVehiculo
{
    /// <summary>Identificador del vehículo (placa o VIN).</summary>
    Identificador,

    /// <summary>Tipo del documento del locatario.</summary>
    TipoDocumento,

    /// <summary>Número del documento del locatario.</summary>
    Documento,
}

/// <summary>Error de una fila: la columna, un código estable y el mensaje para el usuario (nunca repite el documento).</summary>
public sealed record ErrorFilaVehiculo(CampoFilaVehiculo Campo, string Codigo, string Mensaje);

/// <summary>
/// Una fila revalidada por <see cref="ValidadorFilaVehiculo.Validar"/>: el identificador tal como llegó, el normalizado,
/// su clase (null si no es placa ni VIN o si es demasiado largo), el documento del locatario y los errores.
/// </summary>
public sealed record FilaVehiculoValidada(
    string Original, string Normalizado, TipoIdentificador? Tipo, ResultadoDocumento Documento, IReadOnlyList<ErrorFilaVehiculo> Errores)
{
    /// <summary>Verdadero si la fila no tiene ningún error (identificador y documento).</summary>
    public bool EsValida => Errores.Count == 0;

    /// <summary>Sin el número del documento: una fila nunca lo deja en un log.</summary>
    public override string ToString() =>
        $"FilaVehiculoValidada {{ Original = {Original}, Normalizado = {Normalizado}, Tipo = {Tipo}, Errores = {string.Join(",", Errores.Select(e => e.Codigo))} }}";
}

/// <summary>
/// Regla de una fila capturada del lote (HU #13749 para el análisis de la carga; extraída en HU #13751 para que la
/// revalidación de filas, la confirmación del lote y la captura Express usen exactamente la misma). El identificador se
/// clasifica con <see cref="ValidadorIdentificador"/> (la regla de Trámites, HU #13740) y, aparte, la regla de entradas
/// demasiado largas (decisión del usuario 2026-10-21): si el valor limpio (mayúsculas, solo A-Z y 0-9, antes de recortar)
/// pasa de <see cref="LargoPlaca"/> caracteres y el validador lo tomó como placa, o de <see cref="LargoVin"/> y lo tomó
/// como VIN, la fila es errónea y nunca se acepta el valor recortado. Tampoco se acepta un identificador escrito con más
/// de <see cref="LargoOriginal"/> caracteres (el ancho de <c>identificador_original</c>). Después, la forma del documento
/// del locatario (<see cref="ValidadorDocumentoPersona"/>).
/// </summary>
/// <example>
/// <code>
/// ValidadorFilaVehiculo.Validar("abc-123", null, null)   // → válida, ABC123, PlacaCarro
/// ValidadorFilaVehiculo.Validar("AB1234567", null, null) // → IDENTIFICADOR_DEMASIADO_LARGO
/// </code>
/// </example>
public static partial class ValidadorFilaVehiculo
{
    public const string CodigoIdentificadorRequerido = "IDENTIFICADOR_REQUERIDO";
    public const string CodigoIdentificadorInvalido = "IDENTIFICADOR_INVALIDO";
    public const string CodigoIdentificadorDemasiadoLargo = "IDENTIFICADOR_DEMASIADO_LARGO";

    public const int LargoPlaca = 8;
    public const int LargoVin = 17;
    public const int LargoOriginal = 32;

    public static FilaVehiculoValidada Validar(string? identificador, string? tipoDocumento, string? documento)
    {
        var original = identificador ?? string.Empty;
        var errores = new List<ErrorFilaVehiculo>();

        var resultado = ValidadorIdentificador.Validar(original);
        var limpio = FueraDePlaca().Replace(original.ToUpperInvariant(), string.Empty);
        TipoIdentificador? tipo = resultado.Tipo;
        var normalizado = resultado.Normalizado;
        if (limpio.Length == 0)
        {
            errores.Add(Error(CodigoIdentificadorRequerido, "Falta el identificador del vehículo (placa o VIN)."));
        }
        else if (tipo is { } detectado && limpio.Length > (detectado == TipoIdentificador.Vin ? LargoVin : LargoPlaca))
        {
            var esVin = detectado == TipoIdentificador.Vin;
            errores.Add(Error(CodigoIdentificadorDemasiadoLargo, esVin
                ? $"El VIN tiene {limpio.Length} caracteres y el máximo es {LargoVin}: revise el valor (no se recorta)."
                : $"La placa tiene {limpio.Length} caracteres y el máximo es {LargoPlaca}: revise el valor (no se recorta)."));
            tipo = null;
            normalizado = limpio;
        }
        else if (tipo is null)
        {
            errores.Add(Error(CodigoIdentificadorInvalido, "El identificador no es una placa ni un VIN válido."));
        }
        else if (original.Trim().Length > LargoOriginal)
        {
            errores.Add(Error(CodigoIdentificadorDemasiadoLargo,
                $"El identificador está escrito con {original.Trim().Length} caracteres y el máximo es {LargoOriginal}: escríbalo sin separadores de más."));
            tipo = null;
        }

        var resultadoDocumento = ValidadorDocumentoPersona.Validar(tipoDocumento, documento);
        if (!resultadoDocumento.EsValido)
        {
            var campo = resultadoDocumento.Codigo == ValidadorDocumentoPersona.CodigoTipoInvalido ? CampoFilaVehiculo.TipoDocumento : CampoFilaVehiculo.Documento;
            errores.Add(new ErrorFilaVehiculo(campo, resultadoDocumento.Codigo!, resultadoDocumento.Mensaje!));
        }

        return new FilaVehiculoValidada(original, normalizado, tipo, resultadoDocumento, errores);
    }

    private static ErrorFilaVehiculo Error(string codigo, string mensaje) => new(CampoFilaVehiculo.Identificador, codigo, mensaje);

    // Limpieza de sanitizePlate antes del corte: mayúsculas y solo A-Z y 0-9.
    [GeneratedRegex(@"[^A-Z0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex FueraDePlaca();
}
