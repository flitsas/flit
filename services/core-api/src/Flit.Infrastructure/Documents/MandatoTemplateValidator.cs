namespace Flit.Infrastructure.Documents;

/// <summary>Una variable <c>{{...}}</c> que no existe en el registro, con su posición en el cuerpo.</summary>
/// <param name="Name">Texto entre llaves tal como se escribió.</param>
/// <param name="Line">Línea (desde 1).</param>
/// <param name="Column">Columna (desde 1) donde empiezan las llaves.</param>
/// <param name="Index">Posición absoluta en el cuerpo (desde 0).</param>
public sealed record MandatoUnknownVariable(string Name, int Line, int Column, int Index);

/// <summary>Resultado de validar el cuerpo de una plantilla de mandato.</summary>
/// <param name="Error">null si es válida; si no, <c>plantilla_vacia</c>, <c>plantilla_sintaxis_invalida</c> o <c>plantilla_variable_invalida</c>.</param>
public sealed record MandatoTemplateValidation(string? Error, IReadOnlyList<MandatoUnknownVariable> UnknownVariables)
{
    public bool IsValid => Error is null;

    public static MandatoTemplateValidation Valid { get; } = new(null, []);
}

/// <summary>
/// Valida el cuerpo de una plantilla de mandato contra el registro único de variables (HU #13170): rechaza lo
/// que el generador dejaría literal o en blanco en el PDF.
/// </summary>
public static class MandatoTemplateValidator
{
    public const string Vacia = "plantilla_vacia";
    public const string SintaxisInvalida = "plantilla_sintaxis_invalida";
    public const string VariableInvalida = "plantilla_variable_invalida";

    /// <summary>Nombres canónicos permitidos, en el orden del registro.</summary>
    public static IReadOnlyList<string> AllowedVariables => MandatoTemplateVariables.All.Select(v => v.Name).ToArray();

    public static MandatoTemplateValidation Validate(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new(Vacia, []);

        var unknown = new List<MandatoUnknownVariable>();
        var i = 0;
        while (i < body.Length)
        {
            var open = body.IndexOf("{{", i, StringComparison.Ordinal);
            if (open < 0)
                break;

            var close = body.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0)
                return new(SintaxisInvalida, []);

            var inner = body.Substring(open + 2, close - open - 2);
            // Una llave de apertura dentro del marcador significa que el primero quedó sin cerrar.
            if (inner.Contains("{{", StringComparison.Ordinal))
                return new(SintaxisInvalida, []);

            // Sin recortar espacios: el generador solo sustituye {{nombre}} exacto, así que {{ placa }} saldría literal.
            if (MandatoTemplateVariables.Find(inner) is null)
            {
                var (line, column) = LineColumn(body, open);
                unknown.Add(new MandatoUnknownVariable(inner, line, column, open));
            }

            i = close + 2;
        }

        return unknown.Count == 0 ? MandatoTemplateValidation.Valid : new(VariableInvalida, unknown);
    }

    private static (int Line, int Column) LineColumn(string body, int index)
    {
        var line = 1;
        var lastBreak = -1;
        for (var k = 0; k < index; k++)
        {
            if (body[k] == '\n')
            {
                line++;
                lastBreak = k;
            }
        }

        return (line, index - lastBreak);
    }
}
