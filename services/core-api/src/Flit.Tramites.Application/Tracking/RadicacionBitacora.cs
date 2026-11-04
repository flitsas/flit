using System.Text.Json;
using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.Tracking;

/// <summary>
/// Fila única de radicación: anterior vacío, nuevo «Radicado».
/// La escriben el envío del borrador y el envío al organismo; la segunda no repite la primera.
/// </summary>
public static class RadicacionBitacora
{
    public const string Etiqueta = "Radicación";
    public const string Campo = "radicacion";
    public const string Nuevo = "Radicado";

    public static bool YaRegistrada(ProcedureInstance instance)
    {
        foreach (var evt in instance.Events)
        {
            if (!string.Equals(evt.Tipo, ProcedureFieldChangeRecorder.EventType, StringComparison.Ordinal))
                continue;
            if (string.IsNullOrWhiteSpace(evt.Payload))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(evt.Payload);
                var root = doc.RootElement;
                if (!root.TryGetProperty("campo", out var campo) || campo.GetString() != Campo)
                    continue;
                if (root.TryGetProperty("nuevo", out var nuevo) && nuevo.GetString() == Nuevo)
                    return true;
            }
            catch (JsonException)
            {
                // Payload ajeno o roto: no es esta fila.
            }
        }

        return false;
    }

    public static ProcedureInstanceEvent Crear(Guid tenantId, Guid instanceId, Guid? changedBy, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            Tipo = ProcedureFieldChangeRecorder.EventType,
            Payload = JsonSerializer.Serialize(new
            {
                etiqueta = Etiqueta,
                campo = Campo,
                anterior = "",
                nuevo = Nuevo,
            }, JsonSerializerOptions.Web),
            CreatedAt = now,
            CreatedBy = changedBy,
        };
}
