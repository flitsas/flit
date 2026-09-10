using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.RuntConfirmation;
using Flit.Tramites.Domain.RuntConfirmation;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.RuntConfirmation;

/// <summary>
/// Lecturas del Historial de Confirmación RUNT (HU #12310). Cross-tenant a propósito: es la pantalla
/// de plataforma (permiso <c>runt_confirmation.history.read</c>), no la de una empresa. Une intento →
/// trámite → tipo → tenant en una sola consulta proyectada; el crudo no viaja en el listado.
/// </summary>
internal sealed class RuntConfirmationHistoryReader(FlitDbContext db) : IRuntConfirmationHistoryReader
{
    public async Task<RuntConfirmationAttemptsPage> ListAttemptsAsync(RuntConfirmationAttemptsQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = Base();

        if (query.RunId is Guid runId)
            q = q.Where(x => x.RunId == runId);
        if (!string.IsNullOrWhiteSpace(query.Verdict))
        {
            var v = query.Verdict.Trim().ToLowerInvariant();
            q = q.Where(x => x.Verdict == v);
        }
        if (!string.IsNullOrWhiteSpace(query.ProcedureTypeCode))
        {
            var code = query.ProcedureTypeCode.Trim().ToUpperInvariant();
            q = q.Where(x => x.ProcedureTypeCode == code);
        }
        if (query.ProcedureInstanceId is Guid instanceId)
            q = q.Where(x => x.ProcedureInstanceId == instanceId);
        if (query.From is DateTimeOffset from)
            q = q.Where(x => x.QueriedAt >= from);
        if (query.To is DateTimeOffset to)
            q = q.Where(x => x.QueriedAt <= to);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToUpperInvariant();
            q = q.Where(x =>
                x.ReferenceNumber.ToUpper().Contains(s)
                || (x.Plate != null && x.Plate.ToUpper() == s)
                || (x.Vin != null && x.Vin.ToUpper() == s));
        }

        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var flats = await q
            .OrderByDescending(x => x.QueriedAt)
            .ThenByDescending(x => x.Id)
            .Skip((query.SafePage - 1) * query.SafePageSize)
            .Take(query.SafePageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new RuntConfirmationAttemptsPage(flats.Select(ToRow).ToList(), total, query.SafePage, query.SafePageSize);
    }

    public async Task<RuntConfirmationAttemptDetail?> GetAttemptAsync(Guid attemptId, CancellationToken ct = default)
    {
        var x = await Base().FirstOrDefaultAsync(j => j.Id == attemptId, ct).ConfigureAwait(false);
        if (x is null)
            return null;

        return new RuntConfirmationAttemptDetail(
            ToRow(x),
            x.RawPayloadId,
            x.SellerRawPayloadId,
            x.ReevaluatedFromAttemptId,
            x.ProcedureStatus,
            x.RuntConfirmedAt,
            x.RuntAttempts,
            x.RuntFlag);
    }

    public async Task<RuntConfirmationRunsPage> ListRunsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var q = db.RuntConfirmationRuns.AsNoTracking();
        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var items = await q.OrderByDescending(r => r.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return new RuntConfirmationRunsPage(items, total, page, pageSize);
    }

    public Task<RuntConfirmationRun?> GetLatestRunAsync(CancellationToken ct = default) =>
        db.RuntConfirmationRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(ct);

    // Proyección PLANA traducible a SQL: nada de records con entidades dentro ni métodos propios en el
    // Select — EF no los traduce y el listado respondería 500.
    private IQueryable<Flat> Base() =>
        from a in db.RuntConfirmationAttempts.AsNoTracking()
        join i in db.ProcedureInstances.AsNoTracking() on a.ProcedureInstanceId equals i.Id
        join t in db.ProcedureTypes.AsNoTracking() on i.ProcedureTypeId equals t.Id
        join te in db.Tenants.AsNoTracking() on a.TenantId equals te.Id into tenants
        from te in tenants.DefaultIfEmpty()
        select new Flat
        {
            Id = a.Id,
            QueriedAt = a.QueriedAt,
            ProcedureInstanceId = a.ProcedureInstanceId,
            ReferenceNumber = i.ReferenceNumber,
            ProcedureTypeCode = t.Code,
            ProcedureTypeName = t.Name,
            Family = t.Family,
            Plate = i.Plate,
            Vin = i.Vin,
            TenantId = a.TenantId,
            TenantName = te != null ? te.LegalName : null,
            ProviderKey = a.ProviderKey,
            QueryKind = a.QueryKind,
            AttemptNo = a.AttemptNo,
            Verdict = a.Verdict,
            ReasonText = a.ReasonText,
            RuleVersion = a.RuleVersion,
            RunId = a.RunId,
            RequestedBy = a.RequestedBy,
            FlagApplied = a.FlagApplied,
            RawPayloadId = a.RawPayloadId,
            SellerRawPayloadId = a.SellerRawPayloadId,
            ReevaluatedFromAttemptId = a.ReevaluatedFromAttemptId,
            ProcedureStatus = i.Status,
            RuntConfirmedAt = i.RuntConfirmedAt,
            RuntAttempts = i.RuntAttempts,
            RuntFlag = i.RuntFlag,
        };

    private static RuntConfirmationAttemptRow ToRow(Flat x) =>
        new(
            x.Id, x.QueriedAt, x.ProcedureInstanceId, x.ReferenceNumber, x.ProcedureTypeCode, x.ProcedureTypeName, x.Family,
            x.Plate, x.Vin, x.TenantId, x.TenantName, x.ProviderKey, x.QueryKind, x.AttemptNo, x.Verdict, x.ReasonText,
            x.RuleVersion, x.RunId, x.RequestedBy, x.FlagApplied, x.RawPayloadId != null || x.SellerRawPayloadId != null);

    private sealed class Flat
    {
        public Guid Id { get; init; }
        public DateTimeOffset QueriedAt { get; init; }
        public Guid ProcedureInstanceId { get; init; }
        public string ReferenceNumber { get; init; } = string.Empty;
        public string ProcedureTypeCode { get; init; } = string.Empty;
        public string ProcedureTypeName { get; init; } = string.Empty;
        public string Family { get; init; } = string.Empty;
        public string? Plate { get; init; }
        public string? Vin { get; init; }
        public Guid TenantId { get; init; }
        public string? TenantName { get; init; }
        public string ProviderKey { get; init; } = string.Empty;
        public string QueryKind { get; init; } = string.Empty;
        public int AttemptNo { get; init; }
        public string Verdict { get; init; } = string.Empty;
        public string ReasonText { get; init; } = string.Empty;
        public string RuleVersion { get; init; } = string.Empty;
        public Guid? RunId { get; init; }
        public Guid? RequestedBy { get; init; }
        public string? FlagApplied { get; init; }
        public Guid? RawPayloadId { get; init; }
        public Guid? SellerRawPayloadId { get; init; }
        public Guid? ReevaluatedFromAttemptId { get; init; }
        public string? ProcedureStatus { get; init; }
        public DateTimeOffset? RuntConfirmedAt { get; init; }
        public int RuntAttempts { get; init; }
        public string? RuntFlag { get; init; }
    }
}
