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
    /// Aplica reglas condicionales de obligatoriedad (HU #10521, RF30) sobre la lista base según
    /// los atributos del trámite (<paramref name="context"/>). Función pura y aditiva:
    /// <list type="bullet">
    ///   <item><c>Require</c>: fuerza obligatorio el ítem (lo agrega como obligatorio si no existía).</item>
    ///   <item><c>Add</c>: agrega el ítem si no existía (respeta su obligatoriedad).</item>
    ///   <item><c>Hide</c>: quita el ítem.</item>
    /// </list>
    /// Sin reglas (o ninguna cuya condición se cumpla) el resultado es idéntico a
    /// <paramref name="baseItems"/> ⇒ sin cambios de comportamiento.
    /// </summary>
    public static IReadOnlyList<ChecklistItem> ApplyConditional(
        IReadOnlyList<ChecklistItem> baseItems,
        TramiteDocumentContext context,
        IReadOnlyList<ConditionalRule>? rules)
    {
        ArgumentNullException.ThrowIfNull(baseItems);
        ArgumentNullException.ThrowIfNull(context);

        if (rules is null || rules.Count == 0)
            return baseItems.ToList();

        var result = baseItems.ToList();

        foreach (var rule in rules)
        {
            if (!rule.When(context))
                continue;

            var index = result.FindIndex(i => i.Id == rule.Item.Id);
            switch (rule.Effect)
            {
                case ConditionalEffect.Hide:
                    if (index >= 0)
                        result.RemoveAt(index);
                    break;

                case ConditionalEffect.Require:
                    if (index >= 0)
                        result[index] = result[index] with { Obligatorio = true };
                    else
                        result.Add(rule.Item with { Obligatorio = true });
                    break;

                case ConditionalEffect.Add:
                    if (index < 0)
                        result.Add(rule.Item);
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// Aplica los parámetros documentales de la compañía gestora (HU #10521, RF31) sobre la lista:
    /// <c>Oculto</c> quita el ítem, <c>Obligatorio</c> lo fuerza obligatorio y <c>Opcional</c> lo
    /// vuelve opcional. Empareja por <c>DocTipo</c> o <c>Id</c>. Función pura y aditiva: sin
    /// parámetros el resultado es idéntico a <paramref name="baseItems"/>.
    /// </summary>
    public static IReadOnlyList<ChecklistItem> ApplyCompanyParams(
        IReadOnlyList<ChecklistItem> baseItems,
        IReadOnlyCollection<CompanyDocumentParam>? parametros)
    {
        ArgumentNullException.ThrowIfNull(baseItems);

        if (parametros is null || parametros.Count == 0)
            return baseItems.ToList();

        var byDoc = parametros
            .GroupBy(p => p.DocTipo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().State, StringComparer.OrdinalIgnoreCase);

        var result = new List<ChecklistItem>();
        foreach (var item in baseItems)
        {
            var key = item.DocTipo ?? item.Id;
            if (!byDoc.TryGetValue(key, out var state) && !byDoc.TryGetValue(item.Id, out state))
            {
                result.Add(item);
                continue;
            }

            switch (state)
            {
                case CompanyDocumentParamState.Oculto:
                    break; // se omite
                case CompanyDocumentParamState.Obligatorio:
                    result.Add(item with { Obligatorio = true });
                    break;
                case CompanyDocumentParamState.Opcional:
                    result.Add(item with { Obligatorio = false });
                    break;
            }
        }

        return result;
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

    /// <summary>
    /// Computa el checklist aplicando, sobre la lista base de la tipología, las reglas
    /// condicionales por atributo del trámite (<paramref name="context"/>, RF30/33/35/37/38/39)
    /// y los parámetros de la compañía gestora (<paramref name="companyParams"/>, RF31), antes de
    /// resolver el estado de satisfacción. Con contexto vacío, sin reglas y sin parámetros el
    /// resultado es idéntico a <see cref="Compute"/> ⇒ sin cambios de comportamiento. Devuelve
    /// <c>null</c> si la tipología no existe.
    /// </summary>
    public static ChecklistResultado? ComputeConditional(
        string? codigo,
        IReadOnlyDictionary<string, bool>? checklistEstado,
        IReadOnlyCollection<string>? docTipos,
        TramiteDocumentContext context,
        IReadOnlyList<ConditionalRule>? rules,
        IReadOnlyCollection<CompanyDocumentParam>? companyParams)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tip = TramiteTipologiaCatalog.Get(codigo);
        if (tip is null)
            return null;

        var conditional = ApplyConditional(tip.Checklist, context, rules);
        var effective = ApplyCompanyParams(conditional, companyParams);
        return ComputeFromItems(tip.Codigo, tip.Nombre, effective, checklistEstado, docTipos);
    }

    /// <summary>
    /// Computa el checklist tomando como base la <b>matriz documental resuelta del gestor</b>
    /// (HU #10522, RF17/RF22 — el gestor manda la lista, obligatoriedad y orden), en vez del
    /// catálogo plano. Sobre esa base aplica —igual que <see cref="ComputeConditional"/>— las
    /// reglas condicionales por atributo del trámite (<paramref name="context"/>) y los parámetros
    /// de la compañía gestora (<paramref name="companyParams"/>), y resuelve la satisfacción. No
    /// depende de <see cref="TramiteTipologiaCatalog"/> (solo lo usa para el nombre visible, con
    /// fallback a <paramref name="codigo"/>), de modo que soporta modalidades que el catálogo en
    /// código aún no describe. <paramref name="matrixItems"/> ya viene ordenada por el resolutor.
    /// </summary>
    public static ChecklistResultado ComputeFromMatrix(
        string? codigo,
        IReadOnlyList<ChecklistItem> matrixItems,
        IReadOnlyDictionary<string, bool>? checklistEstado,
        IReadOnlyCollection<string>? docTipos,
        TramiteDocumentContext context,
        IReadOnlyList<ConditionalRule>? rules,
        IReadOnlyCollection<CompanyDocumentParam>? companyParams)
    {
        ArgumentNullException.ThrowIfNull(matrixItems);
        ArgumentNullException.ThrowIfNull(context);

        var nombre = TramiteTipologiaCatalog.Get(codigo)?.Nombre ?? codigo ?? string.Empty;

        var conditional = ApplyConditional(matrixItems, context, rules);
        var effective = ApplyCompanyParams(conditional, companyParams);
        return ComputeFromItems(codigo ?? string.Empty, nombre, effective, checklistEstado, docTipos);
    }

    private static ChecklistResultado ComputeFromItems(
        string codigo,
        string nombre,
        IReadOnlyList<ChecklistItem> checklistItems,
        IReadOnlyDictionary<string, bool>? checklistEstado,
        IReadOnlyCollection<string>? docTipos)
    {
        var manual = checklistEstado ?? new Dictionary<string, bool>();
        // El emparejamiento docTipo ↔ tipo del adjunto IGNORA mayúsculas, porque los dos extremos no
        // guardan el código igual: `document_types.code` conserva lo que escribió el administrador en
        // el módulo Documental (el saneador solo filtra a [A-Za-z0-9-]), mientras que la subida
        // persiste `procedure_instance_attachments.tipo` en minúsculas. Con comparación sensible, un
        // documento con una sola mayúscula en el código se subía —fichero y fila incluidos— y el
        // checklist seguía pidiéndolo: para el gestor, «no carga».
        //
        // No se arregla normalizando el catálogo: conviven a propósito códigos con distinto casing
        // (`SOAT` del seed de organismos y `soat` del catálogo operativo), así que pasarlos todos a
        // minúsculas los colisionaría contra uq_document_types_code.
        var docs = new HashSet<string>(docTipos ?? [], StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// Quita del checklist de carga los documentos que el sistema genera o apalanca. Siguen
    /// asociados en Documental y salen en el consolidado; no se muestran ni bloquean Requisitos.
    /// </summary>
    public static ChecklistResultado ExcludeFromGestorCarga(
        ChecklistResultado source,
        IReadOnlySet<string> generatedDocTipos)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(generatedDocTipos);
        if (generatedDocTipos.Count == 0 || source.Items.Count == 0)
            return source;

        var excluded = new HashSet<string>(generatedDocTipos, StringComparer.OrdinalIgnoreCase);
        var remaining = source.Items
            .Where(i =>
                string.IsNullOrEmpty(i.Item.DocTipo)
                || !excluded.Contains(i.Item.DocTipo!))
            .ToList();
        if (remaining.Count == source.Items.Count)
            return source;

        var obligatorios = remaining.Where(i => i.Item.Obligatorio).ToList();
        var faltan = obligatorios.Where(i => !i.Satisfecho).Select(i => i.Item.Id).ToList();
        return source with
        {
            Items = remaining,
            Total = remaining.Count,
            Satisfechos = remaining.Count(i => i.Satisfecho),
            ObligatoriosTotal = obligatorios.Count,
            ObligatoriosSatisfechos = obligatorios.Count - faltan.Count,
            FaltanObligatorios = faltan,
            Completo = faltan.Count == 0,
        };
    }
}
