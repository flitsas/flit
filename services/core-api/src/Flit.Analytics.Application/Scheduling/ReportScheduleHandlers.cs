namespace Flit.Analytics.Application.Scheduling;

// Handlers CRUD de informes programados (HU-D Reportes 2.0, §4.7). Patrón del repo:
// POCOs con HandleAsync que devuelven (Result, Error). Error contract con el endpoint:
//   - "not_found"            → 404 (id inexistente o de otro tenant)
//   - cualquier otro string  → 400 con ese detalle en español (validación)
//   - null                   → éxito.

/// <summary>GET /analytics/report-schedules — lista los informes programados del tenant.</summary>
public sealed class ListReportSchedulesHandler(IReportScheduleRepository repo)
{
    public Task<IReadOnlyList<ReportScheduleDto>> HandleAsync(Guid tenantId, CancellationToken ct = default) =>
        repo.ListAsync(tenantId, ct);
}

/// <summary>POST /analytics/report-schedules — crea un informe programado (201).</summary>
public sealed class CreateReportScheduleHandler(IReportScheduleRepository repo)
{
    public async Task<(ReportScheduleDto? Result, string? Error)> HandleAsync(
        Guid tenantId, Guid? createdBy, ReportScheduleInput input, CancellationToken ct = default)
    {
        var (data, error) = SchedulingValidation.ValidateReportSchedule(input);
        if (error is not null)
            return (null, error);

        var dto = await repo.CreateAsync(tenantId, createdBy, data!, ct);
        return (dto, null);
    }
}

/// <summary>PUT /analytics/report-schedules/{id} — actualiza; otro tenant → not_found (404).</summary>
public sealed class UpdateReportScheduleHandler(IReportScheduleRepository repo)
{
    public async Task<(ReportScheduleDto? Result, string? Error)> HandleAsync(
        Guid tenantId, Guid id, ReportScheduleInput input, CancellationToken ct = default)
    {
        var (data, error) = SchedulingValidation.ValidateReportSchedule(input);
        if (error is not null)
            return (null, error);

        var dto = await repo.UpdateAsync(tenantId, id, data!, ct);
        return dto is null ? (null, "not_found") : (dto, null);
    }
}

/// <summary>DELETE /analytics/report-schedules/{id} — borrado lógico; otro tenant → false (404).</summary>
public sealed class DeleteReportScheduleHandler(IReportScheduleRepository repo)
{
    public Task<bool> HandleAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        repo.SoftDeleteAsync(tenantId, id, ct);
}
