namespace Flit.Analytics.Application.Scheduling;

// Handlers CRUD de reglas de alerta + historial de disparos (HU-D Reportes 2.0, §4.7).
// Mismo contrato de error que los handlers de report-schedules:
// "not_found" → 404 · otro string → 400 con detalle en español · null → éxito.

/// <summary>GET /analytics/alert-rules — lista las reglas de alerta del tenant.</summary>
public sealed class ListAlertRulesHandler(IAlertRuleRepository repo)
{
    public Task<IReadOnlyList<AlertRuleDto>> HandleAsync(Guid tenantId, CancellationToken ct = default) =>
        repo.ListAsync(tenantId, ct);
}

/// <summary>POST /analytics/alert-rules — crea una regla de alerta (201).</summary>
public sealed class CreateAlertRuleHandler(IAlertRuleRepository repo)
{
    public async Task<(AlertRuleDto? Result, string? Error)> HandleAsync(
        Guid tenantId, Guid? createdBy, AlertRuleInput input, CancellationToken ct = default)
    {
        var (data, error) = SchedulingValidation.ValidateAlertRule(input);
        if (error is not null)
            return (null, error);

        var dto = await repo.CreateAsync(tenantId, createdBy, data!, ct);
        return (dto, null);
    }
}

/// <summary>PUT /analytics/alert-rules/{id} — actualiza; otro tenant → not_found (404).</summary>
public sealed class UpdateAlertRuleHandler(IAlertRuleRepository repo)
{
    public async Task<(AlertRuleDto? Result, string? Error)> HandleAsync(
        Guid tenantId, Guid id, AlertRuleInput input, CancellationToken ct = default)
    {
        var (data, error) = SchedulingValidation.ValidateAlertRule(input);
        if (error is not null)
            return (null, error);

        var dto = await repo.UpdateAsync(tenantId, id, data!, ct);
        return dto is null ? (null, "not_found") : (dto, null);
    }
}

/// <summary>DELETE /analytics/alert-rules/{id} — borrado lógico; otro tenant → false (404).</summary>
public sealed class DeleteAlertRuleHandler(IAlertRuleRepository repo)
{
    public Task<bool> HandleAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        repo.SoftDeleteAsync(tenantId, id, ct);
}

/// <summary>GET /analytics/alert-events — historial de disparos paginado (más recientes primero).</summary>
public sealed class ListAlertEventsHandler(IAlertRuleRepository repo)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public Task<AlertEventsPageDto> HandleAsync(
        Guid tenantId, Guid? ruleId, int? page, int? pageSize, CancellationToken ct = default)
    {
        var effectivePage = page is > 0 ? page.Value : 1;
        var effectiveSize = pageSize is > 0 ? Math.Min(pageSize.Value, MaxPageSize) : DefaultPageSize;
        return repo.ListEventsAsync(tenantId, ruleId, effectivePage, effectiveSize, ct);
    }
}
