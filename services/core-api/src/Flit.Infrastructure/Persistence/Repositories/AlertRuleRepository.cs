using Flit.Analytics.Application.Scheduling;
using Flit.Infrastructure.Persistence.Entities.Analytics;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// CRUD EF de <c>analytics.alert_rules</c> + lectura paginada de <c>analytics.alert_events</c>
/// (Reportes 2.0, HU-D). Filtro EXPLÍCITO de tenant en toda consulta (además del RLS); un id
/// de otro tenant "no existe" (§4.7 → 404). Borrado lógico con <c>deleted_at</c>.
/// </summary>
internal sealed class AlertRuleRepository(FlitDbContext db) : IAlertRuleRepository
{
    public async Task<IReadOnlyList<AlertRuleDto>> ListAsync(Guid tenantId, CancellationToken ct)
    {
        var rows = await Active(tenantId)
            .OrderBy(r => r.Name).ThenBy(r => r.Id)
            .AsNoTracking()
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<AlertRuleDto> CreateAsync(
        Guid tenantId, Guid? createdBy, ValidatedAlertRule data, CancellationToken ct)
    {
        var entity = new AlertRule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Apply(entity, data);

        db.Set<AlertRule>().Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<AlertRuleDto?> UpdateAsync(
        Guid tenantId, Guid id, ValidatedAlertRule data, CancellationToken ct)
    {
        var entity = await Active(tenantId).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity is null)
            return null;

        Apply(entity, data);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> SoftDeleteAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        var entity = await Active(tenantId).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity is null)
            return false;

        entity.DeletedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = entity.DeletedAt;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<AlertEventsPageDto> ListEventsAsync(
        Guid tenantId, Guid? ruleId, int page, int pageSize, CancellationToken ct)
    {
        // Join con alert_rules para exponer ruleName (contrato AlertEventDto). Incluye eventos
        // de reglas soft-eliminadas: el historial es auditoría y no se borra con la regla.
        var query =
            from e in db.Set<AlertEvent>().AsNoTracking()
            join r in db.Set<AlertRule>().AsNoTracking() on e.AlertRuleId equals r.Id
            where e.TenantId == tenantId && (ruleId == null || e.AlertRuleId == ruleId)
            orderby e.TriggeredAt descending, e.Id descending
            select new { e, r.Name };

        var total = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var items = rows
            .Select(x => new AlertEventDto(
                x.e.Id, x.e.AlertRuleId, x.Name, x.e.TriggeredAt,
                x.e.MetricValue, x.e.Threshold, x.e.Notified, x.e.Message))
            .ToList();

        return new AlertEventsPageDto(items, total);
    }

    private IQueryable<AlertRule> Active(Guid tenantId) =>
        db.Set<AlertRule>().Where(r => r.TenantId == tenantId && r.DeletedAt == null);

    private static void Apply(AlertRule entity, ValidatedAlertRule data)
    {
        entity.Name = data.Name;
        entity.Metric = data.Metric;
        entity.Operator = data.Operator;
        entity.Threshold = data.Threshold;
        entity.WindowMinutes = data.WindowMinutes;
        entity.CooldownMinutes = data.CooldownMinutes;
        entity.Recipients = data.Recipients.ToList();
        entity.IsActive = data.IsActive;
    }

    private static AlertRuleDto ToDto(AlertRule r) => new(
        r.Id, r.Name, r.Metric, r.Operator, r.Threshold, r.WindowMinutes,
        r.CooldownMinutes, r.Recipients, r.IsActive, r.LastTriggeredAt);
}
