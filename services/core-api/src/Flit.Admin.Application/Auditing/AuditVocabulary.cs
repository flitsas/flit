namespace Flit.Admin.Application.Auditing;

/// <summary>
/// Vocabulario estable de la auditoría de configuración (RNF01, ADR-0024). Centraliza los
/// valores de <c>result</c> y <c>operation</c> que persisten en
/// <c>admin.tenant_config_audit_logs</c> para que writers de éxito, writer de fallo y el
/// filtro de la API usen exactamente las mismas cadenas.
/// </summary>
public static class AuditVocabulary
{
    /// <summary>Desenlace de la operación auditada.</summary>
    public static class Results
    {
        public const string Success = "success";
        public const string Failure = "failure";
    }

    /// <summary>Verbo explícito de la operación auditada.</summary>
    public static class Operations
    {
        public const string Create = "create";
        public const string Update = "update";
        public const string Delete = "delete";
    }
}
