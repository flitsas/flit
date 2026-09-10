namespace Flit.Analytics.Application.Scheduling;

/// <summary>
/// Persistencia de informes programados (<c>analytics.report_schedules</c>, HU-D Reportes 2.0).
/// Implementada en Flit.Infrastructure con EF Core. TODAS las operaciones filtran por tenant:
/// un id existente pero de OTRO tenant se comporta como inexistente (contrato §4.7 → 404).
/// El borrado es lógico (<c>deleted_at</c>).
/// <paramref name="tenantId"/>/<c>tenantId</c> es <c>Guid?</c> SOLO por el informe tipo "consulta"
/// con alcance SuperAdmin (§75 del DDL): null es "todas las compañías", NO "sin filtrar" — sigue
/// siendo un filtro exacto, `s.TenantId == null`. Los otros 5 tipos y el alcance "empresa" del
/// tipo "consulta" siguen recibiendo un tenant concreto, como siempre.
/// </summary>
public interface IReportScheduleRepository
{
    Task<IReadOnlyList<ReportScheduleDto>> ListAsync(Guid? tenantId, CancellationToken ct);

    Task<ReportScheduleDto> CreateAsync(
        Guid? tenantId, Guid? createdBy, ValidatedReportSchedule data, CancellationToken ct);

    /// <summary>Null si el schedule no existe (o pertenece a otro tenant / está eliminado).</summary>
    Task<ReportScheduleDto?> UpdateAsync(
        Guid? tenantId, Guid id, ValidatedReportSchedule data, CancellationToken ct);

    /// <summary>False si el schedule no existe (o pertenece a otro tenant / ya está eliminado).</summary>
    Task<bool> SoftDeleteAsync(Guid? tenantId, Guid id, CancellationToken ct);
}
