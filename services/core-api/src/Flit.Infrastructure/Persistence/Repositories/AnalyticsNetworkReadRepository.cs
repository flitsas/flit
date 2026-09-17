using System.Data;
using System.Data.Common;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #12359 (Feature #12257, épica #12235) — estadísticas de la RED de una cabeza de grupo sobre las
/// mismas tablas operacionales que <see cref="AnalyticsReadRepository"/>, con consultas NUEVAS
/// (<c>…NetworkSql</c>) que reciben el conjunto de clientes legibles como UN parámetro de arreglo:
/// <c>WHERE tenant_id = ANY(@tenants)</c> con <c>@tenants</c> tipado <c>uuid[]</c> (AC3). Las
/// consultas y firmas <c>Guid?</c> de siempre no se tocan (AC6/AC7).
/// <para>
/// <b>Conjunto vacío ⇒ resultado vacío sin ir a la base</b> (AC4): la rama de red no tiene «null =
/// todos»; el vacío se resuelve en memoria y, aun si llegara a la base, <c>= ANY('{}'::uuid[])</c>
/// no devuelve filas (equivale a <c>WHERE 1=0</c>).
/// </para>
/// <para>
/// <b>Sin GUC RLS por tenant</b>: <c>app.current_tenant_id</c> es un valor único y aquí no hay un solo
/// tenant. El aislamiento de la red es el filtro explícito por arreglo; el rol de conexión no está
/// sujeto a RLS en <c>tramites.*</c> (sin FORCE ROW LEVEL SECURITY, como documenta el repositorio de
/// siempre), así que fijar el GUC con la cabeza solo daría una falsa sensación de seguridad. Se
/// ejecuta con la conexión abierta sin transacción, igual que la rama global del SuperAdmin.
/// </para>
/// <para>
/// Cada consulta proyecta además <c>tenant_id</c> (o el arreglo de tenants del usuario en el ranking)
/// para que el agregado que ve el cliente sea el mismo de siempre y, aparte, se sepa QUÉ hijos tienen
/// datos en él (<see cref="NetworkAnalyticsResult{T}.ReachedTenantIds"/>, auditoría HU #12361).
/// </para>
/// </summary>
internal sealed class AnalyticsNetworkReadRepository : INetworkAnalyticsReadRepository
{
    private const string TenantsParameter = "tenants";

    /// <summary>Misma normalización familia → categoría que las consultas de siempre (una sola definición).</summary>
    private const string CategoriaCase = AnalyticsReadRepository.CategoriaCase;

    // ── Overview (red) ───────────────────────────────────────────────────────────────────────────────
    // Igual que OverviewSql pero sobre el conjunto y con tenant_id proyectado; la suma por
    // (categoría, estado) se hace en memoria preservando el orden del SQL.
    private const string OverviewNetworkSql = $"""
        SELECT pi.tenant_id,
               {CategoriaCase} AS category,
               pi.status,
               count(*)::int AS total
        FROM tramites.procedure_instances pi
        JOIN tramites.procedure_types pt ON pt.id = pi.procedure_type_id
        WHERE pi.tenant_id = ANY(@tenants)
          AND pi.deleted_at IS NULL
          AND (@from::date IS NULL OR pi.created_at::date >= @from::date)
          AND (@to::date   IS NULL OR pi.created_at::date <= @to::date)
        GROUP BY pi.tenant_id, 2, pi.status
        ORDER BY 2, pi.status, pi.tenant_id;
        """;

    // ── Top Producers (red) ──────────────────────────────────────────────────────────────────────────
    // Mismo vocabulario que TopProducersSql (N 03). Un usuario es UNA fila del ranking aunque haya
    // radicado en varios clientes del conjunto; tenant_ids dice en cuáles.
    private const string TopProducersNetworkSql = """
        SELECT h.changed_by AS user_id, u.display_name,
               count(*) FILTER (WHERE h.to_status = 'entregado')::int                      AS submitted,
               count(*) FILTER (WHERE h.to_status = 'aprobado')::int                       AS approved,
               count(*) FILTER (WHERE h.to_status IN ('rechazado', 'anulado'))::int        AS rejected,
               array_agg(DISTINCT h.tenant_id)                                              AS tenant_ids
        FROM tramites.procedure_instance_status_history h
        JOIN identity.users u ON u.id = h.changed_by
        WHERE h.tenant_id = ANY(@tenants)
          AND h.changed_by IS NOT NULL
          AND h.changed_at::date BETWEEN @from AND @to
        GROUP BY h.changed_by, u.display_name
        HAVING count(*) FILTER (WHERE h.to_status = 'entregado') > 0
        ORDER BY count(*) FILTER (WHERE h.to_status = 'entregado') DESC, u.display_name ASC
        LIMIT @limit;
        """;

    // ── Monthly Trend (red) ──────────────────────────────────────────────────────────────────────────
    private const string MonthlyTrendNetworkSql = $"""
        SELECT pi.tenant_id,
               EXTRACT(YEAR FROM pi.created_at)::int  AS year,
               EXTRACT(MONTH FROM pi.created_at)::int AS month,
               {CategoriaCase} AS category,
               count(*)::int AS total
        FROM tramites.procedure_instances pi
        JOIN tramites.procedure_types pt ON pt.id = pi.procedure_type_id
        WHERE pi.tenant_id = ANY(@tenants)
          AND pi.deleted_at IS NULL
          AND pi.created_at::date BETWEEN @from AND @to
        GROUP BY pi.tenant_id, 2, 3, 4
        ORDER BY 2, 3, 4, pi.tenant_id;
        """;

    private readonly FlitDbContext _context;

    public AnalyticsNetworkReadRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    // ── INetworkAnalyticsReadRepository ──────────────────────────────────────────────────────────────

    public async Task<NetworkAnalyticsResult<IReadOnlyList<CategoryMetricsDto>>> GetNetworkOverviewAsync(
        IReadOnlySet<Guid> tenantIds, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);
        if (tenantIds.Count == 0)
            return NetworkAnalyticsResult<IReadOnlyList<CategoryMetricsDto>>.Empty([]); // AC4: vacío ⇒ nada, sin consulta

        var conn = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = CreateCommand(conn, OverviewNetworkSql);
        AddTenantsParam(cmd, tenantIds);
        AddDateParam(cmd, "from", fromDate);
        AddDateParam(cmd, "to", toDate);

        // Suma en memoria por (categoría, estado) preservando el orden del SQL; tenant_id solo alimenta «alcanzados».
        var reached = new HashSet<Guid>();
        var byCategory = new Dictionary<string, (int Total, List<StatusCountDto> Statuses)>(StringComparer.Ordinal);
        var order = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            reached.Add(reader.GetGuid(0));
            var category = reader.GetString(1);
            var status = reader.GetString(2);
            var count = reader.GetInt32(3);
            if (!byCategory.TryGetValue(category, out var acc))
            {
                acc = (0, new List<StatusCountDto>());
                byCategory[category] = acc;
                order.Add(category);
            }

            var index = acc.Statuses.FindIndex(s => string.Equals(s.Status, status, StringComparison.Ordinal));
            if (index < 0)
                acc.Statuses.Add(new StatusCountDto(status, count));
            else
                acc.Statuses[index] = acc.Statuses[index] with { Count = acc.Statuses[index].Count + count };
            byCategory[category] = (acc.Total + count, acc.Statuses);
        }

        IReadOnlyList<CategoryMetricsDto> items = order
            .Select(c => new CategoryMetricsDto(c, byCategory[c].Total, byCategory[c].Statuses))
            .ToList();
        return new NetworkAnalyticsResult<IReadOnlyList<CategoryMetricsDto>>(items, Ordered(reached));
    }

    public async Task<NetworkAnalyticsResult<IReadOnlyList<TopProducerDto>>> GetNetworkTopProducersAsync(
        IReadOnlySet<Guid> tenantIds, DateOnly fromDate, DateOnly toDate, int limit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);
        if (tenantIds.Count == 0)
            return NetworkAnalyticsResult<IReadOnlyList<TopProducerDto>>.Empty([]); // AC4

        var conn = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = CreateCommand(conn, TopProducersNetworkSql);
        AddTenantsParam(cmd, tenantIds);
        AddParam(cmd, "from", fromDate);
        AddParam(cmd, "to", toDate);
        AddParam(cmd, "limit", limit);

        var reached = new HashSet<Guid>();
        var items = new List<TopProducerDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            items.Add(new TopProducerDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4)));
            foreach (var tenant in reader.GetFieldValue<Guid[]>(5))
                reached.Add(tenant);
        }

        return new NetworkAnalyticsResult<IReadOnlyList<TopProducerDto>>(items, Ordered(reached));
    }

    public async Task<NetworkAnalyticsResult<IReadOnlyList<MonthlyTrendPointDto>>> GetNetworkMonthlyTrendAsync(
        IReadOnlySet<Guid> tenantIds, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);
        if (tenantIds.Count == 0)
            return NetworkAnalyticsResult<IReadOnlyList<MonthlyTrendPointDto>>.Empty([]); // AC4

        var conn = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = CreateCommand(conn, MonthlyTrendNetworkSql);
        AddTenantsParam(cmd, tenantIds);
        AddParam(cmd, "from", fromDate);
        AddParam(cmd, "to", toDate);

        var reached = new HashSet<Guid>();
        var points = new List<MonthlyTrendPointDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            reached.Add(reader.GetGuid(0));
            var year = reader.GetInt32(1);
            var month = reader.GetInt32(2);
            var category = reader.GetString(3);
            var total = reader.GetInt32(4);

            // El SQL viene ordenado por (año, mes, categoría): el punto a sumar, si existe, es el último.
            var last = points.Count - 1;
            if (last >= 0 && points[last].Year == year && points[last].Month == month
                && string.Equals(points[last].Category, category, StringComparison.Ordinal))
                points[last] = points[last] with { Total = points[last].Total + total };
            else
                points.Add(new MonthlyTrendPointDto(year, month, category, total));
        }

        return new NetworkAnalyticsResult<IReadOnlyList<MonthlyTrendPointDto>>(points, Ordered(reached));
    }

    // ── Infraestructura ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Conexión abierta sin transacción ni GUC (ver resumen de la clase: no hay un solo tenant que fijar).</summary>
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

    /// <summary>
    /// BUG #12588 — parámetro de fecha OPCIONAL. Va tipado siempre: un <see cref="DBNull"/> sin
    /// <c>DbType</c> deja al proveedor sin forma de inferir el tipo y la consulta revienta al
    /// prepararse, no al leerse. Con el tipo puesto, el <c>IS NULL</c> del SQL resuelve como debe.
    /// </summary>
    private static void AddDateParam(DbCommand cmd, string name, DateOnly? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = DbType.Date;
        p.Value = (object?)value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static List<Guid> Ordered(HashSet<Guid> tenants) => tenants.OrderBy(t => t).ToList();
}
