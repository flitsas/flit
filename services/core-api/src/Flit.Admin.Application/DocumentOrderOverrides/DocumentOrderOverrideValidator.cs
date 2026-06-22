namespace Flit.Admin.Application.DocumentOrderOverrides;

/// <summary>
/// Validación del campo configurable de un override (POST, HU #10196): <c>orden</c>
/// (smallint ≥ 0). Devuelve el primer mensaje de error o <c>null</c> si es válido; el
/// endpoint lo traduce a 422.
/// </summary>
public static class DocumentOrderOverrideValidator
{
    /// <summary>Tope de <c>sort_order</c> (columna smallint).</summary>
    public const int MaxOrden = short.MaxValue;

    public static string? Validate(int? orden)
    {
        if (orden is null)
        {
            return "El orden es obligatorio.";
        }

        if (orden < 0)
        {
            return "El orden no puede ser negativo.";
        }

        if (orden > MaxOrden)
        {
            return $"El orden no puede superar {MaxOrden}.";
        }

        return null;
    }
}
