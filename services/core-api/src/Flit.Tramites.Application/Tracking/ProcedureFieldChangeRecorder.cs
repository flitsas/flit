using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tracking;

namespace Flit.Tramites.Application.Tracking;

/// <summary>
/// Construye eventos <see cref="ProcedureInstanceEvent"/> de tipo <see cref="EventType"/> a partir
/// de una lista de cambios de campo.
///
/// <para><b>Reglas de filtrado:</b></para>
/// <list type="bullet">
///   <item>Solo campos donde <see cref="ProcedureFieldLabels.EsVisible"/> devuelve <c>true</c>;
///         <c>current_step</c> y claves internas se omiten.</item>
///   <item>Los valores <c>null</c> y blanco se normalizan a cadena vacía antes de comparar.</item>
///   <item>Si anterior y nuevo normalizados son iguales (comparación <c>Ordinal</c>, sensible a
///         mayúsculas), no se genera evento: <c>ABC123</c> y <c>abc123</c> SÍ son distintos.</item>
///   <item>No escribe en base de datos. El llamador es responsable de persistir los eventos
///         con <c>AddEventAsync</c>.</item>
/// </list>
///
/// <para><b>Semántica de grupo:</b> todos los eventos de un mismo guardado comparten
/// <c>CreatedAt</c> y <c>CreatedBy</c>.</para>
/// </summary>
public static class ProcedureFieldChangeRecorder
{
    /// <summary>Tipo del evento de bitácora emitido por este recorder.</summary>
    public const string EventType = "dato_modificado";

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Construye los eventos de cambio de campo que deben añadirse a la bitácora.
    /// </summary>
    /// <param name="tenantId">Tenant al que pertenece el trámite.</param>
    /// <param name="procedureInstanceId">Instancia del trámite.</param>
    /// <param name="changes">Lista de cambios detectados por el llamador.</param>
    /// <param name="actorUserId">Usuario que realizó el cambio; <c>null</c> si es una operación de sistema.</param>
    /// <param name="atUtc">Marca de tiempo del guardado (UTC). Todos los eventos generados la comparten.</param>
    /// <returns>Lista (posiblemente vacía) de eventos listos para persistir.</returns>
    public static IReadOnlyList<ProcedureInstanceEvent> Record(
        Guid tenantId,
        Guid procedureInstanceId,
        IReadOnlyList<ProcedureFieldChange> changes,
        Guid? actorUserId,
        DateTimeOffset atUtc)
    {
        if (changes is null || changes.Count == 0)
            return [];

        var result = new List<ProcedureInstanceEvent>(changes.Count);

        foreach (var change in changes)
        {
            var fieldKey = change.FieldKey;

            // Omitir claves no visibles al usuario (current_step, *_runt, IDs, etc.)
            if (!ProcedureFieldLabels.EsVisible(fieldKey))
                continue;

            // Normalizar: null y blanco son el mismo valor vacío
            var anterior = Normalize(change.Anterior);
            var nuevo    = Normalize(change.Nuevo);

            // Omitir si el valor no cambió (sensible a mayúsculas: ABC123 ≠ abc123)
            if (string.Equals(anterior, nuevo, StringComparison.Ordinal))
                continue;

            var payload = JsonSerializer.Serialize(
                new
                {
                    etiqueta = ProcedureFieldLabels.Etiqueta(fieldKey),
                    campo    = fieldKey,
                    anterior,
                    nuevo,
                },
                _jsonOptions);

            result.Add(new ProcedureInstanceEvent
            {
                Id                  = Guid.NewGuid(),
                TenantId            = tenantId,
                ProcedureInstanceId = procedureInstanceId,
                Tipo                = EventType,
                Payload             = payload,
                CreatedAt           = atUtc,
                CreatedBy           = actorUserId,
            });
        }

        return result;
    }

    // null y blanco → cadena vacía; en otro caso, Trim().
    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
