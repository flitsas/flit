namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Ítem de checklist de una tipología (paridad <c>ChecklistItem</c> de Johan).
/// </summary>
/// <param name="Id">Identificador estable del ítem dentro de la tipología (no traducir).</param>
/// <param name="Label">Etiqueta visible para el gestor.</param>
/// <param name="Obligatorio">Si es obligatorio para enviar a tránsito (gate cuando STRICT).</param>
/// <param name="DocTipo">
/// Tipo de documento que, si está subido, auto-marca este ítem.
/// Si se omite, el ítem solo se satisface con marca manual.
/// </param>
/// <param name="Ayuda">Ayuda contextual / nota normativa (opcional).</param>
public sealed record ChecklistItem(
    string Id,
    string Label,
    bool Obligatorio,
    string? DocTipo = null,
    string? Ayuda = null);
