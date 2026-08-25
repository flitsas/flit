using System.Data.Common;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reporte detallado de trámites (Feature #10813) — lectura en vivo desde
/// <c>analytics.v_procedure_detail_report</c> (HU #10814).
/// </summary>
internal sealed class DetailedReportReadRepository : IDetailedReportReadRepository
{
    // Los saltos de línea inicial y final son OBLIGATORIOS: este fragmento se concatena con las
    // proyecciones (SELECT …) y las cláusulas finales (ORDER BY / GROUP BY). Sin ellos, los raw
    // strings adyacentes se pegan (p. ej. "v.transfer_type" + "FROM …") y Postgres lanza 42601.
    private const string BaseFrom = "\n" + """
        FROM analytics.v_procedure_detail_report v
        WHERE v.tenant_id = @tenant
          AND v.created_at::date BETWEEN @from AND @to
          AND (@office::uuid IS NULL OR v.transit_office_id = @office::uuid)
          AND (@ptype::uuid IS NULL OR v.procedure_type_id = @ptype::uuid)
          AND (@category::text IS NULL OR v.category = @category::text)
          AND (@status::text IS NULL OR v.status = @status::text)
          AND (@reference::text IS NULL OR v.reference_number ILIKE '%' || @reference::text || '%')
          AND (@personDoc::text IS NULL OR v.person_document ILIKE '%' || @personDoc::text || '%')
          AND (@personName::text IS NULL OR v.person_full_name ILIKE '%' || @personName::text || '%')
          AND (@hasTransformation::boolean IS NULL OR v.has_transformation = @hasTransformation::boolean)
          AND (@isLeasing::boolean IS NULL OR v.is_leasing = @isLeasing::boolean)
        """ + "\n";

    private readonly FlitDbContext _context;

    public DetailedReportReadRepository(FlitDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public Task<DetailedProceduresPageDto> GetProceduresAsync(
        DetailedReportFilter filter, int page, int pageSize, CancellationToken ct = default) =>
        ExecuteWithTenantAsync(filter.TenantId, async (conn, tx) =>
        {
            await using var countCmd = CreateCommand(conn, tx, "SELECT count(*)::int " + BaseFrom + ";");
            BindFilter(countCmd, filter);
            var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct).ConfigureAwait(false));

            await using var pageCmd = CreateCommand(conn, tx, """
                SELECT v.id, v.reference_number, v.procedure_type_name, v.category, v.status,
                       v.created_by_display_name, v.submitted_at, v.completed_at,
                       v.person_document, v.person_full_name, v.has_transformation,
                       v.transformation_detail, v.is_leasing, v.payment_type, v.transfer_type
                """ + BaseFrom + """
                ORDER BY v.created_at DESC, v.id DESC
                LIMIT @pageSize OFFSET @offset;
                """);
            BindFilter(pageCmd, filter);
            AddParam(pageCmd, "pageSize", pageSize);
            AddParam(pageCmd, "offset", (page - 1) * pageSize);

            var items = await ReadRowsAsync(pageCmd, ct).ConfigureAwait(false);
            var summary = await ReadSummaryAsync(conn, tx, filter, ct).ConfigureAwait(false);
            return new DetailedProceduresPageDto(items, total, page, pageSize, summary);
        }, ct);

    public Task ExportProceduresAsync(
        DetailedReportFilter filter,
        Func<DetailedProcedureRowDto, CancellationToken, Task> onRowAsync,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(onRowAsync);
        return ExecuteWithTenantAsync(filter.TenantId, async (conn, tx) =>
        {
            await using var cmd = CreateCommand(conn, tx, """
                SELECT v.id, v.reference_number, v.procedure_type_name, v.category, v.status,
                       v.created_by_display_name, v.submitted_at, v.completed_at,
                       v.person_document, v.person_full_name, v.has_transformation,
                       v.transformation_detail, v.is_leasing, v.payment_type, v.transfer_type
                """ + BaseFrom + """
                ORDER BY v.created_at DESC, v.id DESC;
                """);
            BindFilter(cmd, filter);

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var row = ReadRow(reader, ct);
                await onRowAsync(row, ct).ConfigureAwait(false);
            }

            return true;
        }, ct);
    }

    private static async Task<DetailedReportSummaryDto> ReadSummaryAsync(
        DbConnection conn, DbTransaction tx, DetailedReportFilter filter, CancellationToken ct)
    {
        await using var statusCmd = CreateCommand(conn, tx,
            "SELECT v.status, count(*)::int " + BaseFrom + " GROUP BY v.status ORDER BY 2 DESC, 1 ASC;");
        BindFilter(statusCmd, filter);
        var byStatus = await ReadLabelCountsAsync(statusCmd, ct).ConfigureAwait(false);

        await using var catCmd = CreateCommand(conn, tx,
            "SELECT v.category, count(*)::int " + BaseFrom + " GROUP BY v.category ORDER BY 2 DESC, 1 ASC;");
        BindFilter(catCmd, filter);
        var byCategory = await ReadGenericLabelCountsAsync(catCmd, ct).ConfigureAwait(false);

        await using var typeCmd = CreateCommand(conn, tx,
            "SELECT v.procedure_type_name, count(*)::int " + BaseFrom +
            " GROUP BY v.procedure_type_name ORDER BY 2 DESC, 1 ASC;");
        BindFilter(typeCmd, filter);
        var byType = await ReadGenericLabelCountsAsync(typeCmd, ct).ConfigureAwait(false);

        var total = byStatus.Sum(s => s.Count);
        return new DetailedReportSummaryDto(total, byStatus, byCategory, byType);
    }

    private static async Task<IReadOnlyList<StatusCountDto>> ReadLabelCountsAsync(
        DbCommand cmd, CancellationToken ct)
    {
        var items = new List<StatusCountDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            items.Add(new StatusCountDto(reader.GetString(0), reader.GetInt32(1)));
        return items;
    }

    private static async Task<IReadOnlyList<LabelCountDto>> ReadGenericLabelCountsAsync(
        DbCommand cmd, CancellationToken ct)
    {
        var items = new List<LabelCountDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            items.Add(new LabelCountDto(reader.GetString(0), reader.GetInt32(1)));
        return items;
    }

    private static async Task<IReadOnlyList<DetailedProcedureRowDto>> ReadRowsAsync(
        DbCommand cmd, CancellationToken ct)
    {
        var items = new List<DetailedProcedureRowDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            items.Add(ReadRow(reader, ct));
        return items;
    }

    private static DetailedProcedureRowDto ReadRow(DbDataReader reader, CancellationToken ct) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetBoolean(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetBoolean(12),
            reader.GetString(13),
            // `transfer_type` es NULL fuera de la familia TRASPASO (DDL 90): ahí la pregunta no
            // aplica, y una celda vacía lo dice mejor que inventarle una clase de traspaso.
            reader.IsDBNull(14) ? null : reader.GetString(14));

    private static void BindFilter(DbCommand cmd, DetailedReportFilter filter)
    {
        AddParam(cmd, "tenant", filter.TenantId);
        AddParam(cmd, "from", filter.From);
        AddParam(cmd, "to", filter.To);
        AddParam(cmd, "office", (object?)filter.TransitOfficeId ?? DBNull.Value);
        AddParam(cmd, "ptype", (object?)filter.ProcedureTypeId ?? DBNull.Value);
        AddParam(cmd, "category", (object?)filter.Category ?? DBNull.Value);
        AddParam(cmd, "status", (object?)filter.Status ?? DBNull.Value);
        AddParam(cmd, "reference", (object?)filter.ReferenceNumber ?? DBNull.Value);
        AddParam(cmd, "personDoc", (object?)filter.PersonDocument ?? DBNull.Value);
        AddParam(cmd, "personName", (object?)filter.PersonName ?? DBNull.Value);
        AddParam(cmd, "hasTransformation", filter.HasTransformation.HasValue ? filter.HasTransformation.Value : DBNull.Value);
        AddParam(cmd, "isLeasing", filter.IsLeasing.HasValue ? filter.IsLeasing.Value : DBNull.Value);
    }

    private async Task<T> ExecuteWithTenantAsync<T>(
        Guid tenantId, Func<DbConnection, DbTransaction, Task<T>> body, CancellationToken ct)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await _context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)", ct)
                    .ConfigureAwait(false);

                var conn = _context.Database.GetDbConnection();
                var tx = transaction.GetDbTransaction();
                var result = await body(conn, tx).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }

    private static DbCommand CreateCommand(DbConnection conn, DbTransaction tx, string sql)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd;
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
