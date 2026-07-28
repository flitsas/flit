using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// HU #10872 (AC1) — snapshot puro de <c>procedure_instance_field_values</c> (clave→valor canónico)
/// capturado en el momento en que un trámite entra a <c>subsanacion</c>, y su DIFF contra el estado
/// ACTUAL de los field values al re-radicar. El diff es la entrada de
/// <see cref="SubsanacionGateMap.ResolveGates"/>: solo los campos que cambiaron determinan qué gates
/// de negocio se re-evalúan (más el gate final de radicación, que siempre corre).
/// </summary>
public static class FieldValueSnapshot
{
    /// <summary>
    /// Captura clave→valor canónico (<c>ValueText</c>, o <c>ValueJson</c> si el texto viene vacío) de
    /// los field values indicados. Claves case-insensitive (mismo criterio que
    /// <c>PatchFieldValuesHandler</c>/lecturas de <c>FieldKey</c> en el resto del dominio).
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Capture(IEnumerable<ProcedureInstanceFieldValue> fieldValues)
    {
        ArgumentNullException.ThrowIfNull(fieldValues);

        var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var fieldValue in fieldValues)
        {
            snapshot[fieldValue.FieldKey] = CanonicalValue(fieldValue);
        }

        return snapshot;
    }

    private static string? CanonicalValue(ProcedureInstanceFieldValue fieldValue) =>
        !string.IsNullOrEmpty(fieldValue.ValueText) ? fieldValue.ValueText : fieldValue.ValueJson;

    /// <summary>
    /// Claves cuyo valor cambió (agregadas, removidas o modificadas) entre <paramref name="baseline"/>
    /// (el snapshot al entrar a subsanación) y <paramref name="current"/> (el estado actual, al
    /// re-radicar). Comparación de valor exacta (ordinal); claves comparadas case-insensitive.
    /// </summary>
    public static IReadOnlySet<string> Diff(
        IReadOnlyDictionary<string, string?> baseline,
        IReadOnlyDictionary<string, string?> current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in current)
        {
            if (!baseline.TryGetValue(key, out var baseValue) || !string.Equals(baseValue, value, StringComparison.Ordinal))
                changed.Add(key);
        }

        foreach (var key in baseline.Keys)
        {
            if (!current.ContainsKey(key))
                changed.Add(key);
        }

        return changed;
    }
}
