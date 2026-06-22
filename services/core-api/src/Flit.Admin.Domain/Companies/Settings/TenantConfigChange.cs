namespace Flit.Admin.Domain.Companies.Settings;

/// <summary>
/// Cambio individual de un campo de configuración, listo para auditar
/// (<c>admin.tenant_config_audit_logs</c>). <see cref="OldValueJson"/> y
/// <see cref="NewValueJson"/> contienen el valor serializado como JSON (jsonb).
/// </summary>
/// <param name="FieldName">Columna BD modificada (snake_case).</param>
/// <param name="OldValueJson">Valor anterior serializado como JSON.</param>
/// <param name="NewValueJson">Valor nuevo serializado como JSON.</param>
public sealed record TenantConfigChange(string FieldName, string OldValueJson, string NewValueJson);
