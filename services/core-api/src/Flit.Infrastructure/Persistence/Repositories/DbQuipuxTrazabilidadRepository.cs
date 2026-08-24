using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Flit.Modules.Quipux.Domain.LogQx;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Trazabilidad de una radicación (HU #11787): cabecera con sus hermanas, eventos reducidos para la
/// agrupación de hitos, y log completo filtrado y paginado EN SERVIDOR.
///
/// <para><b>Por qué el log se filtra en SQL y no en memoria.</b> Una radicación viva acumula un
/// evento cada diez minutos; el caso de referencia lleva 1.065. Traerlos todos para descartar los
/// que no se muestran anula el propósito del interruptor, y el filtro de «solo errores» tendría que
/// recorrer el histórico entero antes de poder aplicarse (ADR-0051, D2).</para>
///
/// <para><b>Acceso cross-tenant</b>, igual que <see cref="DbQuipuxLogRepository"/>: el rol de
/// core-api es propietario de las tablas y no le aplica su RLS.</para>
/// </summary>
internal sealed class DbQuipuxTrazabilidadRepository(FlitDbContext db) : IQuipuxTrazabilidadRepository
{
    /// <summary>
    /// Predicado del latido, en SQL. Espeja <see cref="QuipuxSondeo.EsLatido"/> — si divergen, el
    /// conteo de ocultos no cuadraría con lo que la línea de hitos agrupa.
    /// </summary>
    private const string LatidoSql = """
        (e.stage LIKE 'consulta%' AND e.outcome = 'ok'
         AND COALESCE((e.detail ->> 'estado_tramite')::int, 1) = 1)
        """;

    public async Task<QuipuxTrazabilidadRadicacion?> GetRadicacionAsync(
        Guid submissionId, CancellationToken cancellationToken = default)
    {
        var row = await (
                from s in db.QuipuxSubmissions.AsNoTracking()
                where s.Id == submissionId
                join pi in db.ProcedureInstances.AsNoTracking() on s.ProcedureInstanceId equals pi.Id
                join pt in db.ProcedureTypes.AsNoTracking() on pi.ProcedureTypeId equals pt.Id
                join t in db.Tenants.AsNoTracking() on pi.TenantId equals t.Id
                join o in db.TransitOffices.AsNoTracking() on pi.TransitOfficeId equals o.Id
                select new
                {
                    s.Id,
                    s.ProcedureInstanceId,
                    pi.ReferenceNumber,
                    ProcedureTypeName = pt.Name,
                    ClientTenantName = t.LegalName,
                    TransitOfficeName = o.Name,
                    DivipoCode = s.DivipoCode ?? o.DivipoCode,
                    s.DocumentName,
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
                    s.UpdatedAt,
                })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        // Hermanas: todas las radicaciones del mismo trámite, de la más antigua a la más nueva. El
        // ordinal se calcula aquí y no en la base porque es una lista corta (un trámite acumula
        // unos pocos intentos, no miles).
        var hermanasRaw = await db.QuipuxSubmissions
            .AsNoTracking()
            .Where(x => x.ProcedureInstanceId == row.ProcedureInstanceId)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Status, x.CreatedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hermanas = hermanasRaw
            .Select((x, i) => new QuipuxRadicacionHermana(x.Id, i + 1, x.Status, x.CreatedAt))
            .ToList();

        var intento = hermanas.FirstOrDefault(h => h.Id == row.Id)?.Intento ?? 1;

        var plate = await ReadPlateAsync(row.ProcedureInstanceId, cancellationToken).ConfigureAwait(false);

        return new QuipuxTrazabilidadRadicacion
        {
            Id = row.Id,
            ProcedureInstanceId = row.ProcedureInstanceId,
            ReferenceNumber = row.ReferenceNumber,
            Plate = plate,
            ProcedureTypeName = row.ProcedureTypeName,
            ClientTenantName = row.ClientTenantName,
            TransitOfficeName = row.TransitOfficeName,
            DivipoCode = row.DivipoCode,
            DocumentoQx = row.DocumentName,
            Status = row.Status,
            Attempts = row.Attempts,
            PollCount = row.PollCount,
            QxRegisterCode = row.QxRegisterCode,
            QxProcedureCode = row.QxProcedureCode,
            RejectionReason = row.RejectionReason,
            CreatedAt = row.CreatedAt,
            RegisteredAt = row.RegisteredAt,
            LastPolledAt = row.LastPolledAt,
            CompletedAt = row.CompletedAt,
            UpdatedAt = row.UpdatedAt,
            Intento = intento,
            TotalIntentos = hermanas.Count,
            Hermanas = hermanas,
        };
    }

    private async Task<string?> ReadPlateAsync(Guid instanceId, CancellationToken ct)
    {
        var rows = await db.ProcedureInstanceFieldValues
            .AsNoTracking()
            .Where(fv => fv.ProcedureInstanceId == instanceId
                && fv.ValueText != null
                && (fv.FieldKey.ToLower().Contains("plac") || fv.FieldKey.ToLower().Contains("plate")))
            .Select(fv => fv.ValueText)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.FirstOrDefault();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Proyecta del <c>detail</c> solo las tres claves que la agrupación y la línea de hitos usan.
    /// Con miles de sondeos, deserializar el jsonb completo de cada uno para tirarlo después sería
    /// el grueso del coste de la pantalla.
    /// </remarks>
    public async Task<IReadOnlyList<QuipuxEventoResumen>> ListEventosParaHitosAsync(
        Guid submissionId, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsRelational())
        {
            return await ListEventosParaHitosInMemoryAsync(submissionId, cancellationToken)
                .ConfigureAwait(false);
        }

        const string sql = """
            SELECT e.stage,
                   e.outcome,
                   e.occurred_at,
                   (e.detail ->> 'duration_ms')::bigint    AS duration_ms,
                   (e.detail ->> 'codigo')::int            AS codigo,
                   (e.detail ->> 'estado_tramite')::int    AS estado_tramite,
                   COALESCE(e.detail ->> 'descripcion', e.detail ->> 'mensaje',
                            e.detail ->> 'motivo')         AS mensaje,
                   e.correlation_id
            FROM tramites.quipux_submission_events e
            WHERE e.submission_id = @submission_id
            ORDER BY e.occurred_at, e.id
            """;

        return await QueryAsync(
                sql,
                [("submission_id", submissionId)],
                r => new QuipuxEventoResumen(
                    r.GetString(0),
                    r.GetString(1),
                    ReadDate(r, 2),
                    r.IsDBNull(3) ? null : r.GetInt64(3),
                    r.IsDBNull(4) ? null : r.GetInt32(4),
                    r.IsDBNull(5) ? null : r.GetInt32(5),
                    r.IsDBNull(6) ? null : r.GetString(6),
                    r.IsDBNull(7) ? null : r.GetGuid(7)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Camino para EF InMemory: el jsonb no se puede proyectar en SQL, así que se deserializa en
    /// memoria. Solo lo usan los tests; en Postgres nunca se toma esta rama.
    /// </summary>
    private async Task<IReadOnlyList<QuipuxEventoResumen>> ListEventosParaHitosInMemoryAsync(
        Guid submissionId, CancellationToken ct)
    {
        var rows = await db.QuipuxSubmissionEvents
            .AsNoTracking()
            .Where(e => e.SubmissionId == submissionId)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .Select(e => new { e.Stage, e.Outcome, e.Detail, e.OccurredAt, e.CorrelationId })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows
            .Select(r =>
            {
                var (ms, codigo, estado, mensaje) = ParseDetail(r.Detail);
                return new QuipuxEventoResumen(
                    r.Stage, r.Outcome, r.OccurredAt, ms, codigo, estado, mensaje, r.CorrelationId);
            })
            .ToList();
    }

    private static (long? Ms, int? Codigo, int? Estado, string? Mensaje) ParseDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return (null, null, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(detail);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null, null, null);
            }

            long? ms = root.TryGetProperty("duration_ms", out var d)
                && d.ValueKind == JsonValueKind.Number && d.TryGetInt64(out var msv) ? msv : null;
            int? codigo = root.TryGetProperty("codigo", out var c)
                && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var cv) ? cv : null;
            int? estado = root.TryGetProperty("estado_tramite", out var e)
                && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var ev) ? ev : null;

            string? mensaje = null;
            foreach (var key in (string[])["descripcion", "mensaje", "motivo"])
            {
                if (root.TryGetProperty(key, out var m) && m.ValueKind == JsonValueKind.String)
                {
                    mensaje = m.GetString();
                    break;
                }
            }

            return (ms, codigo, estado, mensaje);
        }
        catch (JsonException)
        {
            // Detail histórico no-JSON: no aporta nada a los hitos y no debe romper la pantalla.
            return (null, null, null, null);
        }
    }

    public async Task<QuipuxEventosPage> ListEventosAsync(
        QuipuxEventosQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!db.Database.IsRelational())
        {
            return await ListEventosInMemoryAsync(query, cancellationToken).ConfigureAwait(false);
        }

        // Los tres conteos salen de una sola pasada: total de eventos, cuántos son latidos y cuántos
        // casan con el filtro vigente. Separarlos serían tres viajes sobre la misma tabla.
        var filtro = BuildEventFilter(query);

        var countSql = $"""
            SELECT COUNT(*)::int                                     AS total,
                   COUNT(*) FILTER (WHERE {LatidoSql})::int          AS latidos,
                   COUNT(*) FILTER (WHERE {filtro})::int             AS visibles
            FROM tramites.quipux_submission_events e
            WHERE e.submission_id = @submission_id
            """;

        var counts = await QueryAsync(
                countSql,
                [("submission_id", query.SubmissionId)],
                r => (Total: r.GetInt32(0), Latidos: r.GetInt32(1), Visibles: r.GetInt32(2)),
                cancellationToken)
            .ConfigureAwait(false);

        var (total, latidos, visibles) = counts.Count > 0 ? counts[0] : (0, 0, 0);

        // Cuántos se ocultaron: solo cuenta si el interruptor está puesto. Con él apagado no se
        // oculta nada, y decir «0 ocultos» es distinto de decir «no aplica».
        var ocultos = query.OcultarSinNovedad ? latidos : 0;

        if (visibles == 0)
        {
            return new QuipuxEventosPage([], 0, ocultos, total);
        }

        var pageSql = $"""
            SELECT e.stage, e.outcome, e.detail, e.occurred_at, e.correlation_id
            FROM tramites.quipux_submission_events e
            WHERE e.submission_id = @submission_id AND {filtro}
            ORDER BY e.occurred_at, e.id
            OFFSET @offset LIMIT @limit
            """;

        var eventos = await QueryAsync(
                pageSql,
                [
                    ("submission_id", query.SubmissionId),
                    ("offset", (query.Page - 1) * query.PageSize),
                    ("limit", query.PageSize),
                ],
                r => new QuipuxEventoDetallado(
                    r.GetString(0),
                    r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    ReadDate(r, 3),
                    r.IsDBNull(4) ? null : r.GetGuid(4)),
                cancellationToken)
            .ConfigureAwait(false);

        return new QuipuxEventosPage(eventos, visibles, ocultos, total);
    }

    private async Task<QuipuxEventosPage> ListEventosInMemoryAsync(
        QuipuxEventosQuery query, CancellationToken ct)
    {
        var todos = await ListEventosParaHitosInMemoryAsync(query.SubmissionId, ct).ConfigureAwait(false);

        var rows = await db.QuipuxSubmissionEvents
            .AsNoTracking()
            .Where(e => e.SubmissionId == query.SubmissionId)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .Select(e => new QuipuxEventoDetallado(
                e.Stage, e.Outcome, e.Detail, e.OccurredAt, e.CorrelationId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var latidos = todos.Count(QuipuxSondeo.EsLatido);

        var visibles = rows
            .Where((_, i) => !(query.OcultarSinNovedad && QuipuxSondeo.EsLatido(todos[i])))
            .Where(e => !query.SoloErrores || !string.Equals(e.Outcome, "ok", StringComparison.Ordinal))
            .ToList();

        var page = visibles
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return new QuipuxEventosPage(
            page, visibles.Count, query.OcultarSinNovedad ? latidos : 0, rows.Count);
    }

    private static string BuildEventFilter(QuipuxEventosQuery query)
    {
        var conditions = new List<string>();

        if (query.OcultarSinNovedad)
        {
            conditions.Add($"NOT {LatidoSql}");
        }

        if (query.SoloErrores)
        {
            conditions.Add("e.outcome <> 'ok'");
        }

        return conditions.Count == 0 ? "TRUE" : string.Join(" AND ", conditions);
    }

    /// <summary>
    /// Ejecuta y MATERIALIZA en el sitio. Deliberadamente no devuelve el <see cref="DbDataReader"/>:
    /// la conexión es la de EF y sigue viva después, así que el comando y el lector tienen que
    /// cerrarse aquí — devolverlos dejaría al llamador la responsabilidad de liberarlos.
    /// </summary>
    private async Task<List<T>> QueryAsync<T>(
        string sql,
        IReadOnlyList<(string Name, object Value)> parameters,
        Func<DbDataReader, T> map,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
        }

        var results = new List<T>();

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = name;
                p.Value = value;
                cmd.Parameters.Add(p);
            }

            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    results.Add(map(reader));
                }
            }
        }

        return results;
    }

    private static DateTimeOffset ReadDate(DbDataReader r, int ordinal)
    {
        var value = r.GetFieldValue<object>(ordinal);
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero),
            _ => DateTimeOffset.Parse(value.ToString()!, CultureInfo.InvariantCulture),
        };
    }
}
