using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Lectura de <c>procedure_instance_field_values</c> como diccionario por clave, <b>tolerante a
/// duplicados</b>.
///
/// <para>La tabla NO tiene índice único sobre <c>(procedure_instance_id, field_key)</c>: basta con que
/// alguna vez hayan entrado dos filas de la misma clave —dos escrituras concurrentes, una migración,
/// un backfill— para que un <c>ToDictionary</c> directo lance <see cref="ArgumentException"/> y ese
/// trámite se quede sin poder generar documentos NUNCA MÁS. La generación no puede depender de una
/// invariante que la base no garantiza.</para>
///
/// <para>Criterio de desempate: gana el valor NO VACÍO más reciente (<c>UpdatedAt ?? CreatedAt</c>).</para>
/// </summary>
public static class ProcedureFieldValues
{
    public static Dictionary<string, string?> ToDictionary(ProcedureInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return instance.FieldValues
            .GroupBy(f => f.FieldKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(f => !string.IsNullOrWhiteSpace(f.ValueText))
                      .ThenByDescending(f => f.UpdatedAt ?? f.CreatedAt)
                      .First().ValueText,
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Placa para documentos: <c>field_values.plate</c>, luego <c>placa</c>, luego la columna
    /// denormalizada. Vacío o solo espacios → no se inventa texto (matrícula aún sin placa).
    /// </summary>
    public static void EnsurePlaca(Dictionary<string, string?> fv, ProcedureInstance instance)
    {
        ArgumentNullException.ThrowIfNull(fv);
        ArgumentNullException.ThrowIfNull(instance);

        var placa = Get(fv, "plate")
            ?? Get(fv, "placa")
            ?? (string.IsNullOrWhiteSpace(instance.Plate) ? null : instance.Plate.Trim());
        if (placa is null)
            return;

        fv["plate"] = placa;
    }

    public static string? Get(IReadOnlyDictionary<string, string?> fv, string key) =>
        fv.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
}
