namespace Flit.DrFlit.Application.Chat;

/// <summary>
/// Intención que el modelo asigna a un mensaje libre del usuario (ADR-0060 §8.1). Cerrada a propósito:
/// cualquier valor fuera de este enum se trata como fallo del LLM, no como una intención nueva.
/// </summary>
public enum DrFlitIntent
{
    /// <summary>Pregunta de cómo se hace algo: se responde con el manual y se citan slugs.</summary>
    Duda,

    /// <summary>Reporte de error o pedido de ayuda humana: el frontend abre el formulario de caso.</summary>
    Soporte,

    /// <summary>Búsqueda de un trámite, placa, VIN o cliente: el frontend enruta a la sesión Gestión.</summary>
    Gestion,

    /// <summary>Intención ambigua: el modelo responde con una sola pregunta de seguimiento.</summary>
    NoClaro,
}

/// <summary>Valores de <see cref="DrFlitIntent"/> en el contrato JSON (prompt y API).</summary>
public static class DrFlitIntentWire
{
    public const string Duda = "duda";
    public const string Soporte = "soporte";
    public const string Gestion = "gestion";
    public const string NoClaro = "no_claro";

    public static bool TryParse(string? value, out DrFlitIntent intent)
    {
        switch (value)
        {
            case Duda: intent = DrFlitIntent.Duda; return true;
            case Soporte: intent = DrFlitIntent.Soporte; return true;
            case Gestion: intent = DrFlitIntent.Gestion; return true;
            case NoClaro: intent = DrFlitIntent.NoClaro; return true;
            default: intent = default; return false;
        }
    }

    public static string ToWire(this DrFlitIntent intent) => intent switch
    {
        DrFlitIntent.Duda => Duda,
        DrFlitIntent.Soporte => Soporte,
        DrFlitIntent.Gestion => Gestion,
        DrFlitIntent.NoClaro => NoClaro,
        _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, null),
    };
}
