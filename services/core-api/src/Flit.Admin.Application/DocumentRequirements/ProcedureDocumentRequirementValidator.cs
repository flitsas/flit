namespace Flit.Admin.Application.DocumentRequirements;

/// <summary>
/// Validación de los campos configurables de una asociación (POST/PUT, HU #10195):
/// <c>ordenDefault</c> (smallint ≥ 0) y <c>obligatorio</c> (bool requerido). Devuelve
/// el primer mensaje de error o <c>null</c> si es válido; el endpoint lo traduce a 422.
/// </summary>
public static class ProcedureDocumentRequirementValidator
{
    /// <summary>Tope de <c>default_sort_order</c> (columna smallint).</summary>
    public const int MaxOrdenDefault = short.MaxValue;

    public static string? Validate(int? ordenDefault, bool? obligatorio)
    {
        if (ordenDefault is null)
        {
            return "El orden default es obligatorio.";
        }

        if (ordenDefault < 0)
        {
            return "El orden default no puede ser negativo.";
        }

        if (ordenDefault > MaxOrdenDefault)
        {
            return $"El orden default no puede superar {MaxOrdenDefault}.";
        }

        if (obligatorio is null)
        {
            return "El campo obligatorio es requerido.";
        }

        return null;
    }
}
