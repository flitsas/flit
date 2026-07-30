using System.Data.Common;
using System.Text.Json;
using Flit.Analytics.Application.Reporting;
using Flit.Infrastructure.Persistence.Entities.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>Lecturas Reporting V2 sobre analytics.v_reporting_tramites (Feature #11076).</summary>
internal sealed class ReportingReadRepository(FlitDbContext context) : IReportingReadRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public Task<ReportingProceduresPageDto> GetProceduresAsync(
        ReportingProceduresFilter filter, int page, int pageSize, CancellationToken ct = default) =>
        WithTenantAsync(filter.TenantId, async (conn, tx) =>
        {
            var dateCol = filter.DateType switch
            {
                "updated_at" => "v.submitted_at",
                "completed_at" => "v.completed_at",
                _ => "v.created_at",
            };
            var orderCol = filter.SortBy switch
            {
                "status" => "v.status",
                "procedure_type" => "v.procedure_type_name",
                "elapsed_hours" => "v.elapsed_hours_total",
                _ => "v.created_at",
            };
            var orderDir = filter.SortOrder == "asc" ? "ASC" : "DESC";

            var where = $"""
                FROM analytics.v_reporting_tramites v
                WHERE v.tenant_id = @tenant
                  AND COALESCE({dateCol}, v.created_at)::date BETWEEN @from AND @to
                  AND (@office::uuid IS NULL OR v.transit_office_id = @office::uuid)
                  AND (@ptype::text IS NULL OR v.procedure_type_name ILIKE '%' || @ptype::text || '%')
                  AND (@status::text IS NULL OR v.status = @status::text)
                  AND (
                        @search::text IS NULL
                     OR v.plate ILIKE '%' || @search::text || '%'
                     OR v.vin ILIKE '%' || @search::text || '%'
                     OR v.reference_number ILIKE '%' || @search::text || '%'
                     OR v.person_document ILIKE '%' || @search::text || '%'
                     OR v.person_full_name ILIKE '%' || @search::text || '%'
                  )
                """;

            await using var countCmd = Create(conn, tx, "SELECT count(*)::int " + where);
            Bind(countCmd, filter);
            var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

            await using var pageCmd = Create(conn, tx, $"""
                SELECT v.id, v.reference_number, v.procedure_type_name, v.status,
                       v.plate, v.vin, v.transit_office_name, v.company_name,
                       v.person_document, v.person_full_name, v.created_at, v.submitted_at,
                       v.elapsed_hours_total
                {where}
                ORDER BY {orderCol} {orderDir} NULLS LAST, v.id DESC
                LIMIT @pageSize OFFSET @offset
                """);
            Bind(pageCmd, filter);
            Add(pageCmd, "pageSize", pageSize);
            Add(pageCmd, "offset", (page - 1) * pageSize);

            var items = new List<ReportingProcedureRowDto>();
            await using (var reader = await pageCmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    items.Add(new ReportingProcedureRowDto(
                        reader.GetGuid(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        reader.IsDBNull(7) ? null : reader.GetString(7),
                        reader.IsDBNull(8) ? null : reader.GetString(8),
                        reader.IsDBNull(9) ? null : reader.GetString(9),
                        reader.GetFieldValue<DateTimeOffset>(10),
                        reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
                        reader.IsDBNull(12) ? null : reader.GetDouble(12)));
                }
            }

            await using var kpiCmd = Create(conn, tx, $"""
                SELECT
                  count(*)::int,
                  count(*) FILTER (WHERE v.status = 'aprobado')::int,
                  count(*) FILTER (WHERE v.status = 'rechazado')::int,
                  count(*) FILTER (WHERE v.status IN ('borrador','preparado','entregado','subsanacion'))::int,
                  avg(v.elapsed_hours_total)
                {where}
                """);
            Bind(kpiCmd, filter);
            ReportingKpisDto kpis;
            await using (var reader = await kpiCmd.ExecuteReaderAsync(ct))
            {
                await reader.ReadAsync(ct);
                kpis = new ReportingKpisDto(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetDouble(4));
            }

            return new ReportingProceduresPageDto(items, total, page, pageSize, kpis);
        }, ct);

    public Task<ReportingProcedureRowDto?> GetProcedureAsync(
        Guid tenantId, Guid procedureId, CancellationToken ct = default) =>
        WithTenantAsync(tenantId, async (conn, tx) =>
        {
            await using var cmd = Create(conn, tx, """
                SELECT v.id, v.reference_number, v.procedure_type_name, v.status,
                       v.plate, v.vin, v.transit_office_name, v.company_name,
                       v.person_document, v.person_full_name, v.created_at, v.submitted_at,
                       v.elapsed_hours_total
                FROM analytics.v_reporting_tramites v
                WHERE v.tenant_id = @tenant AND v.id = @id
                LIMIT 1
                """);
            Add(cmd, "tenant", tenantId);
            Add(cmd, "id", procedureId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            return new ReportingProcedureRowDto(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetFieldValue<DateTimeOffset>(10),
                reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
                reader.IsDBNull(12) ? null : reader.GetDouble(12));
        }, ct);

    public Task<ReportingAuditDto> GetAuditAsync(
        Guid tenantId, Guid procedureId, CancellationToken ct = default) =>
        WithTenantAsync(tenantId, async (conn, tx) =>
        {
            await using var cmd = Create(conn, tx, """
                SELECT h.changed_at, h.from_status, h.to_status, h.changed_by_user_id,
                       u.display_name, h.role_id_at_time, h.organization_id_at_time,
                       h.organization_type_at_time, h.reason
                FROM tramites.procedure_instance_status_history h
                JOIN tramites.procedure_instances p ON p.id = h.procedure_instance_id
                LEFT JOIN identity.users u ON u.id = h.changed_by_user_id
                WHERE p.tenant_id = @tenant AND h.procedure_instance_id = @id
                ORDER BY h.changed_at ASC, h.id ASC
                """);
            Add(cmd, "tenant", tenantId);
            Add(cmd, "id", procedureId);

            var entries = new List<ReportingAuditEntryDto>();
            var anyHistory = false;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var roleAt = reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5);
                var available = roleAt.HasValue;
                if (available) anyHistory = true;
                entries.Add(new ReportingAuditEntryDto(
                    reader.GetFieldValue<DateTimeOffset>(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    roleAt,
                    reader.IsDBNull(6) ? null : reader.GetGuid(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    available));
            }

            return new ReportingAuditDto(procedureId, anyHistory || entries.Count == 0, entries);
        }, ct);

    public Task<ConsolidadoPageDto> GetConsolidadoAsync(
        Guid tenantId, DateOnly from, DateOnly toDate, string groupBy, CancellationToken ct = default) =>
        WithTenantAsync(tenantId, async (conn, tx) =>
        {
            var (keyExpr, labelExpr) = groupBy switch
            {
                "ot" => ("COALESCE(v.transit_office_id::text,'none')", "COALESCE(NULLIF(v.transit_office_name,''),'Sin OT')"),
                "empresa" => ("v.tenant_id::text", "COALESCE(NULLIF(v.company_name,''),'Sin empresa')"),
                "gestor" => ("COALESCE(v.created_by_display_name,'none')", "COALESCE(v.created_by_display_name,'Sin gestor')"),
                "tipo" => ("COALESCE(v.procedure_type_name,'none')", "COALESCE(v.procedure_type_name,'Sin tipo')"),
                "mes" => ("to_char(v.created_at, 'YYYY-MM')", "to_char(v.created_at, 'YYYY-MM')"),
                _ => ("v.status", "v.status"),
            };

            await using var cmd = Create(conn, tx, $"""
                SELECT {keyExpr} AS k, {labelExpr} AS lbl,
                       count(*)::int,
                       count(*) FILTER (WHERE v.status = 'aprobado')::int,
                       count(*) FILTER (WHERE v.status = 'rechazado')::int,
                       count(*) FILTER (WHERE v.status IN ('borrador','preparado','entregado','subsanacion'))::int,
                       avg(v.elapsed_hours_total)
                FROM analytics.v_reporting_tramites v
                WHERE v.tenant_id = @tenant
                  AND v.created_at::date BETWEEN @from AND @to
                GROUP BY 1, 2
                ORDER BY 3 DESC, 2 ASC
                LIMIT 200
                """);
            Add(cmd, "tenant", tenantId);
            Add(cmd, "from", from);
            Add(cmd, "to", toDate);

            var items = new List<ConsolidadoRowDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                items.Add(new ConsolidadoRowDto(
                    groupBy,
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetDouble(6)));
            }

            return new ConsolidadoPageDto(items, items.Count);
        }, ct);

    public Task<ProductivityPageDto> GetProductivityAsync(
        Guid tenantId, DateOnly from, DateOnly toDate, string dimension, CancellationToken ct = default) =>
        WithTenantAsync(tenantId, async (conn, tx) =>
        {
            var (idExpr, labelExpr) = dimension switch
            {
                "ot" => ("v.transit_office_id", "COALESCE(NULLIF(v.transit_office_name,''),'Sin OT')"),
                "empresa" => ("v.tenant_id", "COALESCE(NULLIF(v.company_name,''),'Sin empresa')"),
                "gestor" => ("NULL::uuid", "COALESCE(v.created_by_display_name,'Sin gestor')"),
                _ => ("NULL::uuid", "COALESCE(v.created_by_display_name,'Sin usuario')"),
            };

            await using var cmd = Create(conn, tx, $"""
                SELECT {idExpr} AS actor_id, {labelExpr} AS lbl,
                       count(*)::int,
                       count(*) FILTER (WHERE v.status = 'aprobado')::int,
                       count(*) FILTER (WHERE v.status = 'rechazado')::int,
                       count(*) FILTER (WHERE v.status IN ('borrador','preparado','entregado','subsanacion'))::int,
                       avg(v.elapsed_hours_total), min(v.elapsed_hours_total), max(v.elapsed_hours_total)
                FROM analytics.v_reporting_tramites v
                WHERE v.tenant_id = @tenant
                  AND v.created_at::date BETWEEN @from AND @to
                GROUP BY 1, 2
                ORDER BY 3 DESC
                LIMIT 100
                """);
            Add(cmd, "tenant", tenantId);
            Add(cmd, "from", from);
            Add(cmd, "to", toDate);

            var items = new List<ProductivityRowDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                items.Add(new ProductivityRowDto(
                    reader.IsDBNull(0) ? null : reader.GetGuid(0),
                    reader.GetString(1),
                    dimension,
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    reader.IsDBNull(7) ? null : reader.GetDouble(7),
                    reader.IsDBNull(8) ? null : reader.GetDouble(8)));
            }

            return new ProductivityPageDto(items);
        }, ct);

    public Task<SlaPageDto> GetSlaAsync(
        Guid tenantId, DateOnly from, DateOnly toDate, CancellationToken ct = default) =>
        WithTenantAsync(tenantId, async (conn, tx) =>
        {
            await using var cmd = Create(conn, tx, """
                SELECT COALESCE(v.procedure_type_name,'Sin tipo') AS ptype,
                       v.transit_office_name,
                       COALESCE(s.sla_hours, 72) AS sla_hours,
                       count(*)::int AS total,
                       count(*) FILTER (WHERE COALESCE(v.elapsed_hours_total,0) <= COALESCE(s.sla_hours, 72))::int AS within_sla,
                       count(*) FILTER (WHERE COALESCE(v.elapsed_hours_total,0) > COALESCE(s.sla_hours, 72))::int AS outside_sla,
                       avg(v.elapsed_hours_total) AS avg_hours
                FROM analytics.v_reporting_tramites v
                LEFT JOIN analytics.report_sla_config s
                  ON s.tenant_id = v.tenant_id
                 AND s.deleted_at IS NULL
                 AND (s.procedure_type IS NULL OR s.procedure_type = v.procedure_type_name)
                 AND (s.transit_office_id IS NULL OR s.transit_office_id = v.transit_office_id)
                 AND (s.effective_to IS NULL OR s.effective_to >= CURRENT_DATE)
                WHERE v.tenant_id = @tenant
                  AND v.created_at::date BETWEEN @from AND @to
                GROUP BY 1, 2, 3
                ORDER BY 4 DESC
                LIMIT 100
                """);
            Add(cmd, "tenant", tenantId);
            Add(cmd, "from", from);
            Add(cmd, "to", toDate);

            var items = new List<SlaRowDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var total = reader.GetInt32(3);
                var within = reader.GetInt32(4);
                items.Add(new SlaRowDto(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetInt16(2),
                    total,
                    within,
                    reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    total == 0 ? 0 : Math.Round(100.0 * within / total, 2)));
            }

            return new SlaPageDto(items);
        }, ct);

    private Task<T> WithTenantAsync<T>(Guid tenantId, Func<DbConnection, DbTransaction, Task<T>> action, CancellationToken ct) =>
        ExecuteWithTenantAsync(tenantId, action, ct);

    private async Task<T> ExecuteWithTenantAsync<T>(
        Guid tenantId, Func<DbConnection, DbTransaction, Task<T>> action, CancellationToken ct)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await context.Database.BeginTransactionAsync(ct);
            var conn = context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(ct);

            await using (var set = conn.CreateCommand())
            {
                set.Transaction = tx.GetDbTransaction();
                set.CommandText = "SELECT set_config('app.current_tenant_id', @t, true)";
                var p = set.CreateParameter();
                p.ParameterName = "t";
                p.Value = tenantId.ToString("D");
                set.Parameters.Add(p);
                await set.ExecuteNonQueryAsync(ct);
            }

            var result = await action(conn, tx.GetDbTransaction());
            await tx.CommitAsync(ct);
            return result;
        });
    }

    private static DbCommand Create(DbConnection conn, DbTransaction tx, string sql)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd;
    }

    private static void Bind(DbCommand cmd, ReportingProceduresFilter filter)
    {
        Add(cmd, "tenant", filter.TenantId);
        Add(cmd, "from", filter.From);
        Add(cmd, "to", filter.To);
        Add(cmd, "office", (object?)filter.TransitOfficeId ?? DBNull.Value);
        Add(cmd, "ptype", (object?)filter.ProcedureType ?? DBNull.Value);
        Add(cmd, "status", (object?)filter.Status ?? DBNull.Value);
        Add(cmd, "search", (object?)filter.Search ?? DBNull.Value);
    }

    private static void Add(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        if (value is DateOnly d && p is NpgsqlParameter np)
            np.NpgsqlDbType = NpgsqlDbType.Date;
        cmd.Parameters.Add(p);
    }
}

internal sealed class ExportJobRepository(FlitDbContext db, IReportingReadRepository read) : IExportJobRepository
{
    public Task<int> CountActiveJobsAsync(Guid ownerUserId, CancellationToken ct = default) =>
        db.ExportJobs.AsNoTracking()
            .CountAsync(j => j.OwnerUserId == ownerUserId
                && j.DeletedAt == null
                && (j.Status == "pending" || j.Status == "processing"), ct);

    public async Task<ExportJobDto> CreateAsync(
        Guid tenantId,
        Guid ownerUserId,
        string reportType,
        string format,
        string filtersJson,
        Guid? correlationId,
        CancellationToken ct = default)
    {
        var entity = new ExportJob
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            OwnerUserId = ownerUserId,
            Status = "pending",
            ReportType = reportType,
            Format = format,
            FiltersJson = filtersJson,
            ProgressPct = 0,
            CorrelationId = correlationId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = ownerUserId,
        };
        db.ExportJobs.Add(entity);
        await db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task NotifyChannelAsync(string channel, Guid jobId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channel))
            throw new ArgumentException("Channel is required.", nameof(channel));

        await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var cmd = new NpgsqlCommand("SELECT pg_notify(@channel, @payload)", conn);
        cmd.Parameters.AddWithValue("channel", channel);
        cmd.Parameters.AddWithValue("payload", jobId.ToString("D"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<long> EstimateRecordCountAsync(
        Guid tenantId, string reportType, string filtersJson, CancellationToken ct = default)
    {
        var (fromRaw, toRaw) = ExportFilterParser.TryParseDates(filtersJson);
        var (from, to, err) = ReportingDateRange.Normalize(fromRaw, toRaw);
        if (err is not null) return 0;

        switch (reportType.ToLowerInvariant())
        {
            case "procedures":
                return await EstimateProceduresAsync(tenantId, from, to, filtersJson, ct).ConfigureAwait(false);
            case "consolidado":
            {
                var groupBy = TryJsonString(filtersJson, "groupBy") ?? "estado";
                var page = await read.GetConsolidadoAsync(tenantId, from, to, groupBy, ct).ConfigureAwait(false);
                return page.Items.Sum(i => (long)i.Total);
            }
            case "productivity":
            {
                var dimension = TryJsonString(filtersJson, "dimension") ?? "usuario";
                var page = await read.GetProductivityAsync(tenantId, from, to, dimension, ct).ConfigureAwait(false);
                return page.Items.Sum(i => (long)i.Total);
            }
            case "sla":
            {
                var page = await read.GetSlaAsync(tenantId, from, to, ct).ConfigureAwait(false);
                return page.Items.Sum(i => (long)i.Total);
            }
            default:
                return 0;
        }
    }

    private async Task<long> EstimateProceduresAsync(
        Guid tenantId, DateOnly from, DateOnly to, string filtersJson, CancellationToken ct)
    {
        Guid? office = null;
        if (Guid.TryParse(TryJsonString(filtersJson, "transitOfficeId"), out var oid))
            office = oid;

        var filter = new ReportingProceduresFilter(
            tenantId,
            from,
            to,
            TryJsonString(filtersJson, "dateType") ?? "created_at",
            office,
            TryJsonString(filtersJson, "procedureType"),
            TryJsonString(filtersJson, "status"),
            TryJsonString(filtersJson, "search"),
            "created_at",
            "desc");

        var page = await read.GetProceduresAsync(filter, 1, 1, ct).ConfigureAwait(false);
        return page.TotalCount;
    }

    private static string? TryJsonString(string filtersJson, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(filtersJson) ? "{}" : filtersJson);
            if (!doc.RootElement.TryGetProperty(name, out var el)
                || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return null;
            var raw = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<ExportJobDto?> GetAsync(Guid jobId, CancellationToken ct = default)
    {
        var entity = await db.ExportJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId && j.DeletedAt == null, ct);
        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<ExportJobDto>> ListByOwnerAsync(Guid ownerUserId, CancellationToken ct = default)
    {
        var items = await db.ExportJobs.AsNoTracking()
            .Where(j => j.OwnerUserId == ownerUserId && j.DeletedAt == null)
            .OrderByDescending(j => j.CreatedAt)
            .Take(50)
            .ToListAsync(ct);
        return items.Select(Map).ToList();
    }

    public async Task<(string? StoragePath, Guid OwnerUserId, string Status)?> GetDownloadMetaAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var entity = await db.ExportJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId && j.DeletedAt == null, ct);
        return entity is null ? null : (entity.FileStoragePath, entity.OwnerUserId, entity.Status);
    }

    private static ExportJobDto Map(ExportJob j) => new(
        j.Id, j.Status, j.ReportType, j.Format, j.ProgressPct, j.CreatedAt, j.CompletedAt, j.ErrorMessage);
}

internal sealed class SavedQueryRepository(FlitDbContext db) : ISavedQueryRepository
{
    public async Task<IReadOnlyList<SavedQueryDto>> ListAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var items = await db.SavedQueries.AsNoTracking()
            .Where(q => q.TenantId == tenantId && q.DeletedAt == null
                && (q.UserId == userId || q.IsShared))
            .OrderByDescending(q => q.CreatedAt)
            .Take(100)
            .ToListAsync(ct);
        return items.Select(Map).ToList();
    }

    public async Task<SavedQueryDto> CreateAsync(
        Guid tenantId, Guid userId, string name, string? description, string filtersJson, bool isShared, CancellationToken ct = default)
    {
        var entity = new SavedQuery
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            UserId = userId,
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            FiltersJson = filtersJson,
            IsShared = isShared,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId,
        };
        db.SavedQueries.Add(entity);
        await db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<SavedQueryDto?> UpdateAsync(
        Guid tenantId, Guid userId, Guid id, string name, string? description, string filtersJson, bool isShared, CancellationToken ct = default)
    {
        var entity = await db.SavedQueries
            .FirstOrDefaultAsync(q => q.Id == id && q.TenantId == tenantId && q.UserId == userId && q.DeletedAt == null, ct);
        if (entity is null) return null;
        entity.Name = name.Trim();
        entity.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        entity.FiltersJson = filtersJson;
        entity.IsShared = isShared;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = userId;
        await db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct = default)
    {
        var entity = await db.SavedQueries
            .FirstOrDefaultAsync(q => q.Id == id && q.TenantId == tenantId && q.UserId == userId && q.DeletedAt == null, ct);
        if (entity is null) return false;
        entity.DeletedAt = DateTimeOffset.UtcNow;
        entity.DeletedBy = userId;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static SavedQueryDto Map(SavedQuery q)
    {
        object filters;
        try { filters = JsonSerializer.Deserialize<object>(q.FiltersJson) ?? new { }; }
        catch { filters = new { }; }
        return new SavedQueryDto(q.Id, q.Name, q.Description, filters, q.IsShared, q.CreatedAt, q.UpdatedAt);
    }
}

internal sealed class DashboardPreferencesRepository(FlitDbContext db) : IDashboardPreferencesRepository
{
    public async Task<DashboardPreferencesDto> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var entity = await db.DashboardPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId && p.DeletedAt == null, ct);
        return new DashboardPreferencesDto(Parse(entity?.ConfigJson));
    }

    public async Task<DashboardPreferencesDto> UpsertAsync(
        Guid tenantId, Guid userId, string configJson, CancellationToken ct = default)
    {
        var entity = await db.DashboardPreferences
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId && p.DeletedAt == null, ct);
        if (entity is null)
        {
            entity = new DashboardPreference
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                UserId = userId,
                ConfigJson = configJson,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = userId,
            };
            db.DashboardPreferences.Add(entity);
        }
        else
        {
            entity.ConfigJson = configJson;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            entity.UpdatedBy = userId;
        }

        await db.SaveChangesAsync(ct);
        return new DashboardPreferencesDto(Parse(entity.ConfigJson));
    }

    private static object Parse(string? json)
    {
        try { return JsonSerializer.Deserialize<object>(json ?? "{}") ?? new { }; }
        catch { return new { }; }
    }
}
