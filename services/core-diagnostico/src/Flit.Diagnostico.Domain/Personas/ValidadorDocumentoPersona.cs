namespace Flit.Diagnostico.Domain.Personas;

/// <summary>
/// Validación de forma del documento de una persona declarado en la captura (HU #13749, F02-H4, R-3): tipo CC, CE, NIT,
/// PAS o TI y número de 1 a 20 caracteres, solo dígitos salvo el pasaporte (letras y dígitos, en mayúsculas). No verifica
/// que la persona exista: eso lo hacen las fuentes al consultar. El tipo se compara sin distinguir mayúsculas y los
/// espacios de los extremos se ignoran; un espacio, punto o guion dentro del número es un error (no se reescribe el
/// documento: un NIT con dígito de verificación se declara sin él). Los mensajes nunca repiten el número.
/// </summary>
/// <example>
/// <code>
/// ValidadorDocumentoPersona.Validar("cc", " 1020304050 ") // → ("CC", "1020304050", null, null)
/// ValidadorDocumentoPersona.Validar("CC", "12AB34")       // → código DOCUMENTO_INVALIDO
/// ValidadorDocumentoPersona.Validar(null, null)           // → Ausente
/// </code>
/// </example>
public static class ValidadorDocumentoPersona
{
    public const string CodigoTipoInvalido = "TIPO_DOCUMENTO_INVALIDO";
    public const string CodigoNumeroInvalido = "DOCUMENTO_INVALIDO";
    public const string CodigoIncompleto = "DOCUMENTO_INCOMPLETO";
    public const int LargoMaximo = 20;

    private const string Pasaporte = "PAS";

    /// <summary>Tipos de documento admitidos, con el código que se guarda.</summary>
    public static IReadOnlyList<string> Tipos { get; } = ["CC", "CE", "NIT", Pasaporte, "TI"];

    /// <summary>Valida la forma del par tipo y número. Sin ninguno de los dos, el documento está ausente.</summary>
    public static ResultadoDocumento Validar(string? tipo, string? numero)
    {
        var t = tipo?.Trim() ?? string.Empty;
        var n = numero?.Trim() ?? string.Empty;
        if (t.Length == 0 && n.Length == 0)
            return ResultadoDocumento.Ausente;
        if (t.Length == 0 || n.Length == 0)
            return Error(CodigoIncompleto, "Declare el tipo y el número del documento del locatario, o deje los dos vacíos.");

        var codigo = Tipos.FirstOrDefault(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase));
        if (codigo is null)
            return Error(CodigoTipoInvalido, $"El tipo de documento no es válido: use {string.Join(", ", Tipos)}.");

        var esPasaporte = codigo == Pasaporte;
        var normalizado = esPasaporte ? n.ToUpperInvariant() : n;
        if (normalizado.Length > LargoMaximo || !normalizado.All(c => char.IsAsciiDigit(c) || (esPasaporte && char.IsAsciiLetterUpper(c))))
        {
            return Error(CodigoNumeroInvalido, esPasaporte
                ? $"El número de pasaporte solo admite letras y dígitos, hasta {LargoMaximo} caracteres."
                : $"El número de documento {codigo} solo admite dígitos, hasta {LargoMaximo}, sin puntos, guiones ni espacios.");
        }

        return new ResultadoDocumento(codigo, normalizado, null, null);
    }

    private static ResultadoDocumento Error(string codigo, string mensaje) => new(null, null, codigo, mensaje);
}
