using Flit.Modules.Quipux.Domain.Envios;
using Flit.Modules.Quipux.Domain.LogQx;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lectura del LOG QX (HU #10793): busca radicaciones por placa/trámite/radicado y las devuelve con
/// su bitácora de eventos. Solo consulta — sin claim, sin transiciones.
///
/// <para><b>Acceso cross-tenant.</b> Igual que <see cref="DbQuipuxSubmissionConsoleRepository"/>:
/// <c>tramites.quipux_submissions</c>, <c>procedure_instances</c> y <c>procedure_instance_field_values</c>
/// llevan RLS <c>tenant_isolation</c>, pero el rol de core-api es PROPIETARIO y las tablas no declaran
/// <c>FORCE ROW LEVEL SECURITY</c>, así que la política no se le aplica y el soporte ve todos los
/// tenants. El acotado lo dan los filtros explícitos de la búsqueda, no el GUC.</para>
///
/// <para><b>Placa.</b> No es columna: se proyecta de <c>procedure_instance_field_values</c>. La
/// <c>field_key</c> de la placa es data-driven por tipo de trámite, así que aquí se acepta cualquier
/// clave "placa"/"plate" (los trámites por VIN no tienen placa y quedan con <c>null</c>). Se casa por
/// valor normalizado (mayúsculas, sin espacios) para que la búsqueda no dependa de cómo se tecleó.</para>
/// </summary>
internal sealed class DbQuipuxLogRepository(FlitDbContext db) : IQuipuxLogRepository
{
    public async Task<QuipuxLogPage> SearchAsync(
        QuipuxLogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<QuipuxSubmission> scoped = db.QuipuxSubmissions.AsNoTracking();

        if (query.ProcedureInstanceId is { } instanceId)
        {
            scoped = scoped.Where(s => s.ProcedureInstanceId == instanceId);
        }

        if (!string.IsNullOrWhiteSpace(query.Radicado))
        {
            // El radicado ≈ código QX (registro o trámite). Si no es numérico no puede casar con
            // ningún código: se responde página vacía en vez de ignorar el filtro.
            if (!int.TryParse(query.Radicado, out var radicado))
            {
                return new QuipuxLogPage([], 0);
            }

            scoped = scoped.Where(s => s.QxRegisterCode == radicado || s.QxProcedureCode == radicado);
        }

        if (!string.IsNullOrWhiteSpace(query.Placa))
        {
            var placa = query.Placa.Trim().ToUpper();
            var instanceIds =
                from fv in db.ProcedureInstanceFieldValues.AsNoTracking()
                where fv.ValueText != null
                    && fv.ValueText.ToUpper() == placa
                    && (fv.FieldKey.ToLower().Contains("plac") || fv.FieldKey.ToLower().Contains("plate"))
                select fv.ProcedureInstanceId;

            scoped = scoped.Where(s => instanceIds.Contains(s.ProcedureInstanceId));
        }

        var total = await scoped.CountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
        {
            return new QuipuxLogPage([], 0);
        }

        var rows = await (
                from s in scoped
                join pi in db.ProcedureInstances.AsNoTracking() on s.ProcedureInstanceId equals pi.Id
                join pt in db.ProcedureTypes.AsNoTracking() on pi.ProcedureTypeId equals pt.Id
                join t in db.Tenants.AsNoTracking() on pi.TenantId equals t.Id
                orderby s.CreatedAt descending
                select new SubmissionRow(
                    s.Id,
                    s.ProcedureInstanceId,
                    pi.ReferenceNumber,
                    pt.Name,
                    t.LegalName,
                    s.DocumentName,
                    s.DivipoCode,
                    s.Status,
                    s.Attempts,
                    s.PollCount,
                    s.QxRegisterCode,
                    s.QxProcedureCode,
                    s.RejectionReason,
                    s.CreatedAt,
                    s.RegisteredAt,
                    s.LastPolledAt,
                    s.CompletedAt,
                    s.UpdatedAt))
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var submissionIds = rows.Select(r => r.Id).ToList();
        var instanceIdsInPage = rows.Select(r => r.ProcedureInstanceId).Distinct().ToList();

        // Eventos de las radicaciones de la página, ordenados como la línea de tiempo.
        var eventRows = await db.QuipuxSubmissionEvents
            .AsNoTracking()
            .Where(e => submissionIds.Contains(e.SubmissionId))
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .Select(e => new EventRow(
                e.SubmissionId, e.Stage, e.Outcome, e.Detail, e.CorrelationId, e.OccurredAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var eventsBySubmission = eventRows
            .GroupBy(e => e.SubmissionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Placa por trámite (primera clave "placa"/"plate" con valor).
        var plateRows = await (
                from fv in db.ProcedureInstanceFieldValues.AsNoTracking()
                where instanceIdsInPage.Contains(fv.ProcedureInstanceId)
                    && fv.ValueText != null
                    && (fv.FieldKey.ToLower().Contains("plac") || fv.FieldKey.ToLower().Contains("plate"))
                select new { fv.ProcedureInstanceId, fv.ValueText })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var plateByInstance = plateRows
            .GroupBy(p => p.ProcedureInstanceId)
            .ToDictionary(g => g.Key, g => g.First().ValueText);

        var entries = rows
            .Select(r => new QuipuxLogEntry
            {
                Id = r.Id,
                ProcedureInstanceId = r.ProcedureInstanceId,
                ReferenceNumber = r.ReferenceNumber,
                ProcedureTypeName = r.ProcedureTypeName,
                ClientTenantName = r.ClientTenantName,
                Plate = plateByInstance.GetValueOrDefault(r.ProcedureInstanceId),
                DocumentName = r.DocumentName,
                DivipoCode = r.DivipoCode,
                Status = r.Status,
                Attempts = r.Attempts,
                PollCount = r.PollCount,
                QxRegisterCode = r.QxRegisterCode,
                QxProcedureCode = r.QxProcedureCode,
                RejectionReason = r.RejectionReason,
                CreatedAt = r.CreatedAt,
                RegisteredAt = r.RegisteredAt,
                LastPolledAt = r.LastPolledAt,
                CompletedAt = r.CompletedAt,
                UpdatedAt = r.UpdatedAt,
                Events = MapEvents(eventsBySubmission.GetValueOrDefault(r.Id)),
            })
            .ToList();

        return new QuipuxLogPage(entries, total);
    }

    private static List<QuipuxLogEvent> MapEvents(List<EventRow>? events)
    {
        if (events is null || events.Count == 0)
        {
            return [];
        }

        return events
            .Select(e => new QuipuxLogEvent
            {
                Stage = e.Stage,
                Outcome = e.Outcome,
                Detail = e.Detail,
                CorrelationId = e.CorrelationId,
                OccurredAt = e.OccurredAt,
            })
            .ToList();
    }

    private sealed record SubmissionRow(
        Guid Id,
        Guid ProcedureInstanceId,
        string ReferenceNumber,
        string ProcedureTypeName,
        string ClientTenantName,
        string DocumentName,
        string? DivipoCode,
        string Status,
        int Attempts,
        int PollCount,
        int? QxRegisterCode,
        int? QxProcedureCode,
        string? RejectionReason,
        DateTimeOffset CreatedAt,
        DateTimeOffset? RegisteredAt,
        DateTimeOffset? LastPolledAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? UpdatedAt);

    private sealed record EventRow(
        Guid SubmissionId,
        string Stage,
        string Outcome,
        string? Detail,
        Guid? CorrelationId,
        DateTimeOffset OccurredAt);
}
