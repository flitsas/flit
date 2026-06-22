namespace Flit.Admin.Domain.DocumentOrderOverrides;

/// <summary>
/// Constantes y utilidades del ámbito (scope) de un override de orden documental
/// (HU #10196). El scope determina a qué nivel aplica la personalización: por Organismo
/// de Tránsito (<see cref="Ot"/>) o por Cliente/tenant (<see cref="Cliente"/>). Los
/// valores se persisten <b>exactamente</b> en <c>document_order_overrides.scope_type</c>.
/// <see cref="Default"/> no es un scope persistible: identifica el nivel base
/// (<c>procedure_document_requirements</c>) en la matriz resuelta.
/// </summary>
public static class DocumentOrderScope
{
    /// <summary>Override por Organismo de Tránsito — <c>scope_type='OT'</c> (RF09–RF12).</summary>
    public const string Ot = "OT";

    /// <summary>Override por Cliente (tenant) — <c>scope_type='CLIENTE'</c> (RF13–RF16).</summary>
    public const string Cliente = "CLIENTE";

    /// <summary>Nivel base sin override (matriz resuelta, RF18). No se persiste.</summary>
    public const string Default = "DEFAULT";

    /// <summary>True si <paramref name="scope"/> es un scope persistible válido (OT | CLIENTE).</summary>
    public static bool IsValid(string? scope) => scope is Ot or Cliente;

    /// <summary>
    /// Normaliza el valor recibido por query (trim + mayúsculas) y lo devuelve si es un
    /// scope válido; en caso contrario devuelve <c>null</c> (el endpoint responde 400).
    /// </summary>
    public static string? Normalize(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return null;
        }

        var normalized = scope.Trim().ToUpperInvariant();
        return IsValid(normalized) ? normalized : null;
    }
}
