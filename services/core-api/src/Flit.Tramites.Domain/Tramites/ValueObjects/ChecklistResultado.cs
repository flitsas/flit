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
    bool Completo)
{
    /// <summary>
    /// Checklist de un trámite cuyo tipo todavía no tiene documentos configurados: cero ítems y, por
    /// tanto, completo (no falta ningún obligatorio porque no hay ninguno).
    ///
    /// <para>Es un estado legítimo desde ADR-0050: el catálogo en código describe dos tipos y el
    /// resto vive en la matriz documental, que un administrador puede no haber configurado aún. Antes
    /// esa combinación no tenía representación y el endpoint respondía 422.</para>
    /// </summary>
    public static ChecklistResultado Vacio(string? codigo) =>
        new(
            Codigo: codigo ?? string.Empty,
            Nombre: codigo ?? string.Empty,
            Items: [],
            Total: 0,
            Satisfechos: 0,
            ObligatoriosTotal: 0,
            ObligatoriosSatisfechos: 0,
            FaltanObligatorios: [],
            Completo: true);
}
