using Flit.Analytics.Application.Scheduling;
using Flit.Infrastructure.Persistence.Entities.Analytics;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// CRUD EF de <c>analytics.report_schedules</c> (Reportes 2.0, HU-D). Filtro EXPLÍCITO de
/// tenant en toda consulta (además del RLS): un id de otro tenant simplemente "no existe"
/// (§4.7 → 404). Borrado lógico con <c>deleted_at</c>; las filas eliminadas quedan fuera de
/// listados, ediciones y del scheduler.
/// </summary>
internal sealed class ReportScheduleRepository(FlitDbContext db) : IReportScheduleRepository
{
    public async Task<IReadOnlyList<ReportScheduleDto>> ListAsync(Guid? tenantId, CancellationToken ct)
    {
        var rows = await Active(tenantId)
            .OrderBy(s => s.Name).ThenBy(s => s.Id)
            .AsNoTracking()
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<ReportScheduleDto> CreateAsync(
        Guid? tenantId, Guid? createdBy, ValidatedReportSchedule data, CancellationToken ct)
    {
        var entity = new ReportSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Apply(entity, data);

        db.Set<ReportSchedule>().Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<ReportScheduleDto?> UpdateAsync(
        Guid? tenantId, Guid id, ValidatedReportSchedule data, CancellationToken ct)
    {
        var entity = await Active(tenantId).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (entity is null)
            return null;

        Apply(entity, data);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> SoftDeleteAsync(Guid? tenantId, Guid id, CancellationToken ct)
    {
        var entity = await Active(tenantId).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (entity is null)
            return false;

        entity.DeletedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = entity.DeletedAt;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private IQueryable<ReportSchedule> Active(Guid? tenantId) =>
        db.Set<ReportSchedule>().Where(s => s.TenantId == tenantId && s.DeletedAt == null);

    private static void Apply(ReportSchedule entity, ValidatedReportSchedule data)
    {
        entity.Name = data.Name;
        entity.ReportType = data.ReportType;
        entity.Frequency = data.Frequency;
        entity.DayOfWeek = (short?)data.DayOfWeek;
        entity.DayOfMonth = (short?)data.DayOfMonth;
        entity.SendHour = (short)data.SendHour;
        entity.Format = data.Format;
        entity.Recipients = data.Recipients.ToList();
        entity.IsActive = data.IsActive;
        entity.SavedQueryId = data.SavedQueryId;
        entity.SavedQueryScope = data.SavedQueryScope;
    }

    private static ReportScheduleDto ToDto(ReportSchedule s) => new(
        s.Id, s.Name, s.ReportType, s.Frequency, s.DayOfWeek, s.DayOfMonth,
        s.SendHour, s.Format, s.Recipients, s.IsActive, s.LastSentAt,
        s.SavedQueryId, s.SavedQueryScope);
}
