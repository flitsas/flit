using System;
using System.Collections.Generic;
using System.Linq;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Motor de checklist dinámico, puro (sin IO). Paridad <c>computeChecklist</c> /
/// <c>mergeChecklist</c> de Johan. Compartido por API (gate de envío) y UI (progreso).
/// </summary>
public static class ChecklistEngine
{
    /// <summary>
    /// Fusiona el checklist base con un override de organismo (paridad <c>mergeChecklist</c>).
    /// Aplica <c>Hide</c> (oculta ids), <c>Require</c> (fuerza obligatorio) y <c>Add</c>
    /// (agrega ids nuevos, sin duplicar). Función pura.
    /// </summary>
    public static IReadOnlyList<ChecklistItem> Merge(
        IReadOnlyList<ChecklistItem> baseItems,
        ChecklistOverride? @override)
    {
        ArgumentNullException.ThrowIfNull(baseItems);

        if (@override is null)
            return baseItems.ToList();

        var hidden = new HashSet<string>(@override.Hide ?? []);
        var requireSet = new HashSet<string>(@override.Require ?? []);

        var merged = baseItems
            .Where(i => !hidden.Contains(i.Id))
            .Select(i => requireSet.Contains(i.Id) ? i with { Obligatorio = true } : i)
            .ToList();

        var seen = new HashSet<string>(merged.Select(i => i.Id));
        foreach (var item in @override.Add ?? [])
        {
            if (seen.Add(item.Id))
                merged.Add(item);
        }

        return merged;
    }

    /// <summary>
    /// Computa el estado del checklist combinando overrides manuales
    /// (<paramref name="checklistEstado"/>) y documentos subidos
    /// (<paramref name="docTipos"/>, que auto-marcan ítems con <c>DocTipo</c>).
    /// Devuelve <c>null</c> si la tipología no existe. Paridad <c>computeChecklist</c>.
    /// </summary>
    public static ChecklistResultado? Compute(
        string? codigo,
        IReadOnlyDictionary<string, bool>? checklistEstado,
        IReadOnlyCollection<string>? docTipos = null)
    {
        var tip = TramiteTipologiaCatalog.Get(codigo);
        if (tip is null)
            return null;

        return ComputeFromItems(tip.Codigo, tip.Nombre, tip.Checklist, checklistEstado, docTipos);
    }

    /// <summary>
    /// Checklist efectivo con override organismo × tipología
    /// (paridad <c>computeChecklistWithOverride</c>).
    /// </summary>
    public static ChecklistResultado? ComputeWithOverride(
        string? codigo,
        IReadOnlyDictionary<string, bool>? checklistEstado,
        IReadOnlyCollection<string>? docTipos,
        ChecklistOverride? @override)
    {
        var tip = TramiteTipologiaCatalog.Get(codigo);
        if (tip is null)
            return null;

        var effective = Merge(tip.Checklist, @override);
        return ComputeFromItems(tip.Codigo, tip.Nombre, effective, checklistEstado, docTipos);
    }

    private static ChecklistResultado ComputeFromItems(
        string codigo,
        string nombre,
        IReadOnlyList<ChecklistItem> checklistItems,
        IReadOnlyDictionary<string, bool>? checklistEstado,
        IReadOnlyCollection<string>? docTipos)
    {
        var manual = checklistEstado ?? new Dictionary<string, bool>();
        var docs = new HashSet<string>(docTipos ?? []);

        var items = checklistItems.Select(it =>
        {
            bool porDoc = !string.IsNullOrEmpty(it.DocTipo) && docs.Contains(it.DocTipo);
            bool porManual = manual.TryGetValue(it.Id, out var marked) && marked;
            bool satisfecho = porDoc || porManual;
            var via = satisfecho
                ? (porDoc ? ChecklistVia.Documento : ChecklistVia.Manual)
                : ChecklistVia.None;
            return new ChecklistItemComputed(it, satisfecho, via);
        }).ToList();

        var obligatorios = items.Where(i => i.Item.Obligatorio).ToList();
        var faltanObligatorios = obligatorios
            .Where(i => !i.Satisfecho)
            .Select(i => i.Item.Id)
            .ToList();

        return new ChecklistResultado(
            Codigo: codigo,
            Nombre: nombre,
            Items: items,
            Total: items.Count,
            Satisfechos: items.Count(i => i.Satisfecho),
            ObligatoriosTotal: obligatorios.Count,
            ObligatoriosSatisfechos: obligatorios.Count - faltanObligatorios.Count,
            FaltanObligatorios: faltanObligatorios,
            Completo: faltanObligatorios.Count == 0);
    }
}
