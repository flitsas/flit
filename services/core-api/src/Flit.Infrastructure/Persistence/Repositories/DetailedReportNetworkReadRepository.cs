using System.Data.Common;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #12360 (Feature #12257, épica #12235) — reporte detallado de la RED de una cabeza de grupo sobre
/// la misma vista que <see cref="DetailedReportReadRepository"/> (<c>analytics.v_procedure_detail_report</c>)
/// con consultas NUEVAS: el conjunto de clientes legibles viaja como UN parámetro de arreglo
/// (<c>WHERE v.tenant_id = ANY(@tenants)</c>, <c>@tenants</c> tipado <c>uuid[]</c>, AC3) y el resto del
/// predicado es el mismo de siempre. La consulta y la firma <c>DetailedReportFilter.TenantId</c> del
/// repositorio de siempre no se tocan (AC7).
/// <para>
/// <b>Conjunto vacío ⇒ resultado vacío sin ir a la base</b> (AC4): la rama de red no tiene «null = todos»;
/// el vacío se resuelve en memoria y, aun si llegara a la base, <c>= ANY('{}'::uuid[])</c> no devuelve filas.
/// </para>
/// <para>
/// <b>Listado y exportación comparten <see cref="BaseFrom"/></b> (mismo predicado, mismo orden): lo que se
/// exporta es exactamente lo que se consulta con el mismo filtro resuelto (AC5).
/// </para>
/// <para>
/// <b>Sin GUC RLS por tenant</b> ni transacción: igual que <see cref="AnalyticsNetworkReadRepository"/>,
/// aquí no hay un solo tenant que fijar; el aislamiento es el filtro explícito por arreglo.
/// </para>
/// <para>
/// Cada fila proyecta el cliente dueño (<c>tenant_id</c> + razón social, AC1) y ningún campo de documentos
/// ni anexos (AC6). Los clientes DISTINTOS con filas en el conjunto filtrado alimentan
/// <see cref="NetworkAnalyticsResult{T}.ReachedTenantIds"/> (auditoría HU #12361).
/// </para>
/// </summary>
internal sealed class DetailedReportNetworkReadRepository : INetworkDetailedReportReadRepository
{
    private const string TenantsParameter = "tenants";

    // Los saltos de línea inicial y final son OBLIGATORIOS (ver DetailedReportReadRepository): el
    // fragmento se concatena con la proyección y con las cláusulas finales.
    private const string BaseFrom = "\n" + """
        FROM analytics.v_procedure_detail_report v
        JOIN identity.tenants t ON t.id = v.tenant_id
        WHERE v.tenant_id = ANY(@tenants)
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

    private const string RowProjection = """
        SELECT v.tenant_id, t.legal_name,
               v.id, v.reference_number, v.procedure_type_name, v.category, v.status,
               v.created_by_display_name, v.submitted_at, v.completed_at,
               v.person_document, v.person_full_name, v.has_transformation,
               v.transformation_detail, v.is_leasing, v.payment_type, v.transfer_type
        """;

    private const string RowOrder = "ORDER BY v.created_at DESC, v.id DESC";

    private const string PageNetworkSql = RowProjection + BaseFrom + RowOrder + "\nLIMIT @pageSize OFFSET @offset;";

    private const string ExportNetworkSql = RowProjection + BaseFrom + RowOrder + ";";

    private const string CountNetworkSql = "SELECT count(*)::int " + BaseFrom + ";";

    private const string ByStatusNetworkSql = "SELECT v.status, count(*)::int " + BaseFrom + " GROUP BY v.status ORDER BY 2 DESC, 1 ASC;";

    private const string ByCategoryNetworkSql = "SELECT v.category, count(*)::int " + BaseFrom + " GROUP BY v.category ORDER BY 2 DESC, 1 ASC;";

    private const string ByTypeNetworkSql =
        "SELECT v.procedure_type_name, count(*)::int " + BaseFrom + " GROUP BY v.procedure_type_name ORDER BY 2 DESC, 1 ASC;";

    // Desglose por cliente: además de los totales, dice QUÉ clientes tienen filas en el conjunto filtrado.
    private const string ByTenantNetworkSql =
        "SELECT v.tenant_id, t.legal_name, count(*)::int " + BaseFrom + " GROUP BY v.tenant_id, t.legal_name ORDER BY 3 DESC, 2 ASC;";

    private readonly FlitDbContext _context;

    public DetailedReportNetworkReadRepository(FlitDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<NetworkAnalyticsResult<NetworkDetailedProceduresPageDto>> GetNetworkProceduresAsync(
        NetworkDetailedReportFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var scope = ToScope(filter);
        if (filter.TenantIds.Count == 0)
        {
            // AC4: conjunto vacío ⇒ reporte sin registros, sin consulta.
            var emptySummary = new NetworkDetailedReportSummaryDto(0, [], [], [], []);
            return NetworkAnalyticsResult<NetworkDetailedProceduresPageDto>.Empty(
                new NetworkDetailedProceduresPageDto([], 0, page, pageSize, emptySummary, scope));
        }

        var conn = await OpenConnectionAsync(ct).ConfigureAwait(false);

        await using var countCmd = CreateCommand(conn, CountNetworkSql);
        BindFilter(countCmd, filter);
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct).ConfigureAwait(false));

        await using var pageCmd = CreateCommand(conn, PageNetworkSql);
        BindFilter(pageCmd, filter);
        AddParam(pageCmd, "pageSize", pageSize);
        AddParam(pageCmd, "offset", (page - 1) * pageSize);
        var items = await ReadRowsAsync(pageCmd, ct).ConfigureAwait(false);

        var summary = await ReadSummaryAsync(conn, filter, ct).ConfigureAwait(false);
        var reached = summary.ByTenant.Select(t => t.TenantId).OrderBy(t => t).ToList();

        return new NetworkAnalyticsResult<NetworkDetailedProceduresPageDto>(
            new NetworkDetailedProceduresPageDto(items, total, page, pageSize, summary, scope),
            reached);
    }

    public async Task<IReadOnlyList<Guid>> ExportNetworkProceduresAsync(
        NetworkDetailedReportFilter filter,
        Func<NetworkDetailedProcedureRowDto, CancellationToken, Task> onRowAsync,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(onRowAsync);
        if (filter.TenantIds.Count == 0)
            return []; // AC4

        var conn = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = CreateCommand(conn, ExportNetworkSql);
        BindFilter(cmd, filter);

        var reached = new HashSet<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var row = ReadRow(reader);
            reached.Add(row.TenantId);
            await onRowAsync(row, ct).ConfigureAwait(false);
        }

        return reached.OrderBy(t => t).ToList();
    }

    // ── lectura ───────────────────────────────────────────────────────────────────────────────

    private static async Task<NetworkDetailedReportSummaryDto> ReadSummaryAsync(
        DbConnection conn, NetworkDetailedReportFilter filter, CancellationToken ct)
    {
        await using var statusCmd = CreateCommand(conn, ByStatusNetworkSql);
        BindFilter(statusCmd, filter);
        var byStatus = new List<StatusCountDto>();
        await using (var reader = await statusCmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                byStatus.Add(new StatusCountDto(reader.GetString(0), reader.GetInt32(1)));
        }

        await using var catCmd = CreateCommand(conn, ByCategoryNetworkSql);
        BindFilter(catCmd, filter);
        var byCategory = await ReadLabelCountsAsync(catCmd, ct).ConfigureAwait(false);

        await using var typeCmd = CreateCommand(conn, ByTypeNetworkSql);
        BindFilter(typeCmd, filter);
        var byType = await ReadLabelCountsAsync(typeCmd, ct).ConfigureAwait(false);

        await using var tenantCmd = CreateCommand(conn, ByTenantNetworkSql);
        BindFilter(tenantCmd, filter);
        var byTenant = new List<TenantCountDto>();
        await using (var reader = await tenantCmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                byTenant.Add(new TenantCountDto(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2)));
        }

        var total = byStatus.Sum(s => s.Count);
        return new NetworkDetailedReportSummaryDto(total, byStatus, byCategory, byType, byTenant);
    }

    private static async Task<IReadOnlyList<LabelCountDto>> ReadLabelCountsAsync(DbCommand cmd, CancellationToken ct)
    {
        var items = new List<LabelCountDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            items.Add(new LabelCountDto(reader.GetString(0), reader.GetInt32(1)));
        return items;
    }

    private static async Task<IReadOnlyList<NetworkDetailedProcedureRowDto>> ReadRowsAsync(DbCommand cmd, CancellationToken ct)
    {
        var items = new List<NetworkDetailedProcedureRowDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            items.Add(ReadRow(reader));
        return items;
    }

    /// <summary>Misma lectura de columnas que la fila de siempre, desplazada dos posiciones por el cliente dueño.</summary>
    private static NetworkDetailedProcedureRowDto ReadRow(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetBoolean(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.GetBoolean(14),
            reader.GetString(15),
            reader.IsDBNull(16) ? null : reader.GetString(16));

    // ── infraestructura ───────────────────────────────────────────────────────────────────────

    private static NetworkScopeDto ToScope(NetworkDetailedReportFilter filter) =>
        new(filter.TenantIds.OrderBy(t => t).ToList());

    /// <summary>Conexión abierta sin transacción ni GUC (no hay un solo tenant que fijar).</summary>
    private async Task<DbConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var conn = _context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }

    private static DbCommand CreateCommand(DbConnection conn, string sql)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    /// <summary>Mismos parámetros que <c>DetailedReportReadRepository.BindFilter</c>, con el arreglo en lugar de <c>@tenant</c>.</summary>
    private static void BindFilter(DbCommand cmd, NetworkDetailedReportFilter filter)
    {
        AddTenantsParam(cmd, filter.TenantIds);
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

    /// <summary>
    /// AC3 — el conjunto de clientes viaja como UN parámetro <c>uuid[]</c> (<c>= ANY(@tenants)</c>);
    /// nunca como lista interpolada en el texto. Tipado explícito para que Npgsql no tenga que inferirlo.
    /// </summary>
    private static void AddTenantsParam(DbCommand cmd, IReadOnlySet<Guid> tenantIds)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = TenantsParameter;
        p.Value = tenantIds.ToArray();
        if (p is NpgsqlParameter np)
            np.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid;
        cmd.Parameters.Add(p);
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
