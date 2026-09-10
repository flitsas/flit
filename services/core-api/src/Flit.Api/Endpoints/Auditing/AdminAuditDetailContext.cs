using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Flit.Api.Endpoints.Auditing;

/// <summary>
/// Canal para que un endpoint le pase a <see cref="AdminAuditFilter"/> el DETALLE del cambio
/// (valor anterior / valor nuevo), que el filtro no puede deducir por sí solo porque solo ve el
/// status HTTP. Mismo patrón de <c>HttpContext.Items</c> que <see cref="ConfigAuditFailureContext"/>.
/// </summary>
/// <remarks>
/// Sin esto el rastro respondía "quién" y "cuándo" pero no "qué cambió": las columnas
/// <c>old_value</c> / <c>new_value</c> de <c>admin.tenant_config_audit_logs</c> se escribían
/// siempre en <c>null</c> para las operaciones administrativas (solo la auditoría de
/// configuración las poblaba). Es opcional: los endpoints que no lo usan siguen auditando igual.
/// </remarks>
internal static class AdminAuditDetailContext
{
    private const string OldValueKey = "flit.audit.old_value";
    private const string NewValueKey = "flit.audit.new_value";

    /// <summary>
    /// Registra el detalle del cambio. Los valores se serializan a JSON porque las columnas
    /// destino son <c>jsonb</c>. Nunca propaga excepciones: auditar es best-effort y no debe
    /// tumbar la operación principal.
    /// </summary>
    public static void SetChange(HttpContext httpContext, object? oldValue, object? newValue)
    {
        try
        {
            if (oldValue is not null)
            {
                httpContext.Items[OldValueKey] = JsonSerializer.Serialize(oldValue);
            }

            if (newValue is not null)
            {
                httpContext.Items[NewValueKey] = JsonSerializer.Serialize(newValue);
            }
        }
        catch (NotSupportedException)
        {
            // Tipo no serializable: se audita la operación sin detalle.
        }
    }

    public static string? GetOldValue(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(OldValueKey, out var value) ? value as string : null;

    public static string? GetNewValue(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(NewValueKey, out var value) ? value as string : null;
}
