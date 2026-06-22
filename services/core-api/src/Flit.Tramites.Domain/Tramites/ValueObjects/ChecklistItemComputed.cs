namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>Origen de la satisfacción de un ítem de checklist (para UI).</summary>
public enum ChecklistVia
{
    None,
    Manual,
    Documento,
}

/// <summary>
/// Ítem de checklist con su estado computado (paridad <c>ChecklistItemComputed</c> de Johan).
/// </summary>
/// <param name="Item">Definición base del ítem.</param>
/// <param name="Satisfecho">Marcado manualmente O documento <c>DocTipo</c> ya subido.</param>
/// <param name="Via">Origen de la satisfacción.</param>
public sealed record ChecklistItemComputed(
    ChecklistItem Item,
    bool Satisfecho,
    ChecklistVia Via);
