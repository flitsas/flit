namespace Flit.Infrastructure.Security;

/// <summary>
/// Matriz RBAC Reporting V2 (Feature #11076 / HU #11105 / diseño §9).
/// Fuente única para seed C#, migración SQL y tests de contrato.
/// </summary>
internal static class ReportingV2RbacCatalog
{
    public const string ModuleCode = "reportes-v2";
    public const string ModuleName = "Reportería Transaccional V2";
    public const int ExpectedReportingSlugCount = 15;

    public static readonly IReadOnlyList<(string Slug, string Name, string Method, string Route, string Scope)> ReportingSlugs =
    [
        ("reporting.read", "Ver reportes V2", "GET", "/api/v1/reporting/procedures*", "tenant"),
        ("reporting.detail", "Ver detalle de trámite en reportes", "GET", "/api/v1/reporting/procedures/{id}", "tenant"),
        ("reporting.export", "Solicitar/listar exportaciones", "POST", "/api/v1/reporting/exports*", "tenant"),
        ("reporting.export.download", "Descargar exportación", "GET", "/api/v1/reporting/exports/{id}/download-url", "tenant"),
        ("reporting.saved-queries.read", "Ver consultas guardadas", "GET", "/api/v1/reporting/saved-queries*", "tenant"),
        ("reporting.saved-queries.write", "Gestionar consultas guardadas", "POST", "/api/v1/reporting/saved-queries*", "tenant"),
        ("reporting.schedules.read", "Ver informes programados V2", "GET", "/api/v1/reporting/schedules*", "tenant"),
        ("reporting.schedules.write", "Gestionar informes programados V2", "POST", "/api/v1/reporting/schedules*", "tenant"),
        ("reporting.alerts.read", "Ver alertas V2", "GET", "/api/v1/reporting/alerts*", "tenant"),
        ("reporting.alerts.write", "Gestionar alertas V2", "POST", "/api/v1/reporting/alerts*", "tenant"),
        ("reporting.dashboard.preferences", "Preferencias de dashboard", "GET", "/api/v1/reporting/preferences*", "tenant"),
        ("reporting.audit", "Auditoría operacional de trámites", "GET", "/api/v1/reporting/procedures/{id}/audit*", "tenant"),
        ("reporting.consolidado", "Reporte consolidado/volumetría", "GET", "/api/v1/reporting/consolidado*", "tenant"),
        ("reporting.productivity", "Reporte de productividad V2", "GET", "/api/v1/reporting/productivity*", "tenant"),
        ("reporting.global", "Vista global multi-tenant", "GET", "/api/v1/reporting/*", "global"),
    ];

    /// <summary>Slugs legados a mantener inactivos (AC3).</summary>
    public static readonly IReadOnlyList<(string Slug, string Name, string Method, string Route)> LegacyDetailedReportSlugs =
    [
        ("detailed-report.read", "Ver reportes detallados (legado)", "GET", "/api/v1/detailed-report/procedures"),
        ("detailed-report.export", "Exportar reportes detallados (legado)", "GET", "/api/v1/detailed-report/procedures/export"),
    ];

    public static IReadOnlyList<string> ReportingSlugNames =>
        ReportingSlugs.Select(s => s.Slug).ToArray();
}
