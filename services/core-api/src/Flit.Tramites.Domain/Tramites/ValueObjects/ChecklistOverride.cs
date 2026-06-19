using System.Collections.Generic;

namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Override organismo × tipología (paridad <c>ChecklistOverride</c> de Johan).
/// Persistido en BD como JSON; fusionado por <c>ChecklistEngine.Merge</c>.
/// </summary>
/// <param name="Add">Ítems adicionales exclusivos del STT (ids nuevos).</param>
/// <param name="Hide">IDs del catálogo base que este STT no exige.</param>
/// <param name="Require">IDs que pasan a obligatorios aunque en base sean opcionales.</param>
public sealed record ChecklistOverride(
    IReadOnlyList<ChecklistItem>? Add = null,
    IReadOnlyList<string>? Hide = null,
    IReadOnlyList<string>? Require = null);
