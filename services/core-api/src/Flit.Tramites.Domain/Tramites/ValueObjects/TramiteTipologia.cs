using System.Collections.Generic;

namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Definición de una tipología de trámite con su checklist (paridad <c>TramiteTipologia</c> de Johan).
/// </summary>
public sealed record TramiteTipologia(
    string Codigo,
    string Nombre,
    string Descripcion,
    IReadOnlyList<ChecklistItem> Checklist);
