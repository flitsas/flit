using System.Globalization;
using System.Text;
using System.Text.Json;
using Flit.Admin.Application.Auditing;

namespace Flit.Admin.Application.Plataforma.PersonaNatural;

/// <summary>
/// Auditoría común de la configuración del portal de Persona Natural (Feature J, Épica #14021): cada cambio queda en el
/// rastro unificado <c>admin.tenant_config_audit_logs</c> con actor, fecha, valor anterior y valor nuevo. Los valores se
/// escriben como JSON de campos simples (sin reflexión: el proyecto es compatible con AOT) y NUNCA llevan datos personales.
/// </summary>
internal static class PersonaNaturalAuditoria
{
    /// <summary>JSON de un objeto plano: cadena, número, booleano, fecha, Guid o null por campo.</summary>
    public static string Json(params (string Campo, object? Valor)[] campos)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            foreach (var (campo, valor) in campos)
            {
                switch (valor)
                {
                    case null: w.WriteNull(campo); break;
                    case bool b: w.WriteBoolean(campo, b); break;
                    case int i: w.WriteNumber(campo, i); break;
                    case long l: w.WriteNumber(campo, l); break;
                    case DateOnly d: w.WriteString(campo, d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); break;
                    case DateTimeOffset o: w.WriteString(campo, o.ToString("O", CultureInfo.InvariantCulture)); break;
                    default: w.WriteString(campo, Convert.ToString(valor, CultureInfo.InvariantCulture)); break;
                }
            }

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static Task EscribirAsync(
        IAdminAuditWriter writer, string entidad, string operacion, Guid actor, Guid? objetivo, string? anterior, string? nuevo, CancellationToken ct) =>
        writer.WriteAsync(
            new AdminAuditEntry(
                TenantId: null,
                TenantType: null,
                Module: AuditVocabulary.Modules.Config,
                EntityName: entidad,
                Operation: operacion,
                Result: AuditVocabulary.Results.Success,
                ErrorCode: null,
                ActorUserId: actor,
                TargetEntityType: "PERSONA_NATURAL_PORTAL",
                TargetEntityId: objetivo,
                ClientIp: null,
                UserAgent: null,
                OldValue: anterior,
                NewValue: nuevo),
            ct);
}
