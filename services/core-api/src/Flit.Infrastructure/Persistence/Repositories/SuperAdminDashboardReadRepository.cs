using System.Data;
using System.Data.Common;
using Flit.Analytics.Application.Queries.SuperAdminDashboard;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lectura ENTRE TENANTS del Inicio del SuperAdmin (rediseño de los 3 dashboards). Es el sitio a revisar si se sospecha
/// una fuga desde ese Inicio: aquí se cruzan todas las compañías a propósito, y solo lo alcanzan los endpoints con
/// <c>SuperAdminPolicy</c>.
/// <para>Mismo bypass que <see cref="SuperAdminTenantScope"/>: transacción con <c>SET LOCAL row_security = off</c>. Si el
/// rol de conexión estuviera sujeto a RLS, PostgreSQL rechaza la consulta en vez de devolver una porción de los datos
/// (falla cerrada). Con <c>tenantId</c> el filtro va explícito en el SQL (<c>pi.tenant_id = @tenant</c>).</para>
/// <para>Universo: trámites no borrados acotados por fecha de CREACIÓN (como <c>/analytics/overview</c>) y por familia
/// visible (HU #13664). Vocabulario de los estados: ver <c>SuperAdminDashboardDtos.cs</c>.</para>
/// </summary>
internal sealed class SuperAdminDashboardReadRepository : ISuperAdminDashboardReadRepository
{
    private const string CategoriaCase = AnalyticsReadRepository.CategoriaCase;
    private const string FamilyByType = AnalyticsFamilyFilter.ByType;

    /// <summary>Estados «en proceso»: todo lo que no es aprobado, rechazado ni un final sin resultado.</summary>
    private const string EnProceso = "status NOT IN ('aprobado', 'rechazado', 'anulado', 'revocado')";

    private const string InstsCte = $"""
        WITH insts AS (
            SELECT pi.id, pi.tenant_id, pi.status, pi.created_at, pi.transit_office_id,
                   {CategoriaCase} AS category
            FROM tramites.procedure_instances pi
            JOIN tramites.procedure_types pt ON pt.id = pi.procedure_type_id
            WHERE pi.deleted_at IS NULL
              AND (@tenant::uuid IS NULL OR pi.tenant_id = @tenant::uuid)
              AND (@from::date IS NULL OR pi.created_at::date >= @from::date)
              AND (@to::date   IS NULL OR pi.created_at::date <= @to::date)
              AND {FamilyByType}
        )
        """;

    // Decisión = transición a 'aprobado'/'rechazado'; se mide desde la ÚLTIMA radicación ('entregado') anterior, como
    // DecidedCte de AnalyticsMetricsReadRepository. Una instancia rechazada y vuelta a aprobar aporta dos decisiones.
    private const string DecidedCte = InstsCte + """
        , decided AS (
            SELECT i.transit_office_id,
                   EXTRACT(EPOCH FROM (h.changed_at - d.delivered_at)) / 86400.0 AS days
            FROM tramites.procedure_instance_status_history h
            JOIN insts i ON i.id = h.procedure_instance_id
            JOIN LATERAL (
                SELECT max(h2.changed_at) AS delivered_at
                FROM tramites.procedure_instance_status_history h2
                WHERE h2.procedure_instance_id = h.procedure_instance_id
                  AND h2.to_status = 'entregado'
                  AND h2.changed_at <= h.changed_at
            ) d ON d.delivered_at IS NOT NULL
            WHERE h.to_status IN ('aprobado', 'rechazado')
        )
        """;

    private const string OfficesSql =
        // 1) Volumen y distribución por organismo y familia.
        InstsCte + $"""
        SELECT i.transit_office_id, tofc.name, i.category,
               count(*)::int,
               count(*) FILTER (WHERE i.status = 'aprobado')::int,
               count(*) FILTER (WHERE i.{EnProceso})::int,
               count(*) FILTER (WHERE i.status = 'rechazado')::int
        FROM insts i
        JOIN catalogs.transit_offices tofc ON tofc.id = i.transit_office_id
        GROUP BY i.transit_office_id, tofc.name, i.category
        ORDER BY tofc.name, i.transit_office_id, i.category;
        """ +
        // 2) Tiempo de decisión por organismo.
        DecidedCte + """
        SELECT transit_office_id, count(*)::int, avg(days)::float8
        FROM decided
        WHERE transit_office_id IS NOT NULL
        GROUP BY transit_office_id;
        """ +
        // 3) Tiempo del ecosistema: todas las decisiones del universo.
        DecidedCte + """
        SELECT count(*)::int, avg(days)::float8 FROM decided;
        """;

    // Compañías: el universo de las Consultas de SuperAdmin (activas y sin perfil de organismo). Represado = en proceso
    // y sin transición (o sin crearse, si no tiene historial) hace más de @stuckDays días.
    private const string CompaniesSql = InstsCte + $"""
        , companias AS (
            SELECT t.id, t.legal_name
            FROM identity.tenants t
            WHERE t.is_active
              AND NOT EXISTS (SELECT 1 FROM admin.transit_office_profiles p WHERE p.tenant_id = t.id)
        ), activos AS (
            SELECT i.id, i.tenant_id,
                   coalesce((SELECT max(h.changed_at)
                             FROM tramites.procedure_instance_status_history h
                             WHERE h.procedure_instance_id = i.id), i.created_at) AS last_changed_at
            FROM insts i
            WHERE i.{EnProceso}
        )
        SELECT c.id, c.legal_name,
               (SELECT count(*) FROM insts i WHERE i.tenant_id = c.id)::int,
               (SELECT count(*) FROM activos a WHERE a.tenant_id = c.id)::int,
               (SELECT count(*) FROM insts i WHERE i.tenant_id = c.id AND i.status = 'aprobado')::int,
               (SELECT count(*) FROM insts i WHERE i.tenant_id = c.id AND i.status = 'rechazado')::int,
               (SELECT count(*) FROM activos a
                 WHERE a.tenant_id = c.id
                   AND a.last_changed_at < now() - make_interval(days => @stuckDays))::int
        FROM companias c
        ORDER BY 4 DESC, c.legal_name ASC, c.id ASC;
        """;

    private readonly FlitDbContext _context;

    public SuperAdminDashboardReadRepository(FlitDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public Task<IReadOnlyList<SuperAdminDashboardCompanyDto>> GetCompaniesAsync(
        DateOnly? fromDate, DateOnly? toDate, int stuckDays,
        IReadOnlyList<string>? allowedFamilies = null, CancellationToken ct = default) =>
        ReadCrossTenantAsync<IReadOnlyList<SuperAdminDashboardCompanyDto>>(async (conn, tx) =>
        {
            await using var cmd = CreateCommand(conn, tx, CompaniesSql);
            AddGuidParam(cmd, "tenant", null);
            AddDateParam(cmd, "from", fromDate);
            AddDateParam(cmd, "to", toDate);
            AddParam(cmd, "stuckDays", stuckDays);
            AnalyticsFamilyFilter.AddParam(cmd, allowedFamilies);

            var items = new List<SuperAdminDashboardCompanyDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var aprobados = reader.GetInt32(4);
                var rechazados = reader.GetInt32(5);
                var decididos = aprobados + rechazados;
                items.Add(new SuperAdminDashboardCompanyDto(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    aprobados,
                    rechazados,
                    decididos == 0 ? null : Math.Round(aprobados * 100.0 / decididos, 1, MidpointRounding.AwayFromZero),
                    reader.GetInt32(6)));
            }

            return items;
        }, ct);

    public Task<SuperAdminDashboardOfficesDto> GetOfficesAsync(
        Guid? tenantId, DateOnly? fromDate, DateOnly? toDate,
        IReadOnlyList<string>? allowedFamilies = null, CancellationToken ct = default) =>
        ReadCrossTenantAsync(async (conn, tx) =>
        {
            await using var cmd = CreateCommand(conn, tx, OfficesSql);
            AddGuidParam(cmd, "tenant", tenantId);
            AddDateParam(cmd, "from", fromDate);
            AddDateParam(cmd, "to", toDate);
            AnalyticsFamilyFilter.AddParam(cmd, allowedFamilies);

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            // 1) Volumen y distribución.
            var offices = new Dictionary<Guid, (string Nombre, int Volumen, List<SuperAdminDashboardFamilyCountsDto> Familias)>();
            var order = new List<Guid>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = reader.GetGuid(0);
                if (!offices.TryGetValue(id, out var office))
                {
                    office = (reader.GetString(1), 0, []);
                    order.Add(id);
                }

                office.Volumen += reader.GetInt32(3);
                office.Familias.Add(new SuperAdminDashboardFamilyCountsDto(
                    reader.GetString(2), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6)));
                offices[id] = office;
            }

            // 2) Tiempos por organismo.
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            var times = new Dictionary<Guid, (int Decididos, double? Dias)>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                times[reader.GetGuid(0)] = (reader.GetInt32(1), NullableDouble(reader, 2));

            // 3) Ecosistema.
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            var decididos = 0;
            double? dias = null;
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                decididos = reader.GetInt32(0);
                dias = NullableDouble(reader, 1);
            }

            var items = order
                .Select(id =>
                {
                    var o = offices[id];
                    var t = times.GetValueOrDefault(id);
                    return new SuperAdminDashboardOfficeDto(id, o.Nombre, o.Volumen, t.Decididos, Round1(t.Dias), o.Familias);
                })
                .OrderByDescending(o => o.Volumen)
                .ThenBy(o => o.Nombre, StringComparer.Ordinal)
                .ToList();

            return new SuperAdminDashboardOfficesDto(tenantId, fromDate, toDate, decididos, Round1(dias), items);
        }, ct);

    // ── Infraestructura ─────────────────────────────────────────────────────────────────────────────

    private async Task<T> ReadCrossTenantAsync<T>(Func<DbConnection, DbTransaction, Task<T>> body, CancellationToken ct)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await _context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await _context.Database.ExecuteSqlRawAsync("SET LOCAL row_security = off", ct).ConfigureAwait(false);
                var result = await body(_context.Database.GetDbConnection(), transaction.GetDbTransaction())
                    .ConfigureAwait(false);
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

    /// <summary>Parámetros opcionales SIEMPRE tipados: un DBNull sin DbType no se prepara (BUG #12588).</summary>
    private static void AddDateParam(DbCommand cmd, string name, DateOnly? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = DbType.Date;
        p.Value = (object?)value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    private static void AddGuidParam(DbCommand cmd, string name, Guid? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = DbType.Guid;
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

    private static double? NullableDouble(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private static double? Round1(double? value) =>
        value is { } v ? Math.Round(v, 1, MidpointRounding.AwayFromZero) : null;
}
