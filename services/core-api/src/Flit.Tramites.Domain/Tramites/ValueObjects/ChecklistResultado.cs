using System.Collections.Generic;

namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Resultado del cómputo de checklist de un trámite (paridad <c>ChecklistResultado</c> de Johan).
/// <para><c>FaltanObligatorios</c>: IDs de obligatorios aún sin satisfacer (bloquean envío a tránsito si STRICT).</para>
/// <para><c>Completo</c>: todos los obligatorios satisfechos.</para>
/// </summary>
public sealed record ChecklistResultado(
    string Codigo,
    string Nombre,
    IReadOnlyList<ChecklistItemComputed> Items,
    int Total,
    int Satisfechos,
    int ObligatoriosTotal,
    int ObligatoriosSatisfechos,
    IReadOnlyList<string> FaltanObligatorios,
    bool Completo);
