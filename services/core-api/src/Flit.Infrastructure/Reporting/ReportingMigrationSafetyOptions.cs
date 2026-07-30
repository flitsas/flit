namespace Flit.Infrastructure.Reporting;

/// <summary>
/// Opciones de seguridad para las migraciones del subsistema de reportería (Feature #11076).
/// Sección <c>Reporting:MigrationSafety</c> de appsettings.
///
/// <para>
/// <strong>Propósito:</strong> telemetría y advertencia operativa. Estos valores NO modifican
/// el schema de base de datos ni bifurcan rutas de migración — la migración siempre aplica
/// el mismo DDL determinista independientemente de los valores configurados aquí.
/// </para>
///
/// <para>
/// <strong>StatusHistoryRowWarningThreshold:</strong> si el número de filas en
/// <c>tramites.procedure_instance_status_history</c> supera este umbral en el momento de
/// ejecutar la migración, se emite un log de nivel Warning para alertar al equipo de
/// infra sobre el tamaño de la tabla (útil para planificar mantenimiento de índices).
/// El valor default (500 000) representa una tabla mediana en la que un
/// <c>ADD COLUMN IF NOT EXISTS</c> es O(1) en PG17 (sin reescritura de heap).
/// </para>
/// </summary>
public sealed class ReportingMigrationSafetyOptions
{
    public const string SectionName = "Reporting:MigrationSafety";

    /// <summary>
    /// Umbral de filas en <c>procedure_instance_status_history</c> a partir del cual se
    /// emite un Warning de telemetría (no bloquea ni cambia el schema).
    /// Default: 500 000.
    /// </summary>
    public long StatusHistoryRowWarningThreshold { get; set; } = 500_000;
}
