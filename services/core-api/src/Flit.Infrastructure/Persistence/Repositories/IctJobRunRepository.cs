using System.Data;
using System.Data.Common;
using Flit.Admin.Domain.Ict;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lee <c>ict.job_runs</c> (HU #12513). SQL parametrizado; no selecciona error_message.
/// </summary>
internal sealed class IctJobRunRepository(FlitDbContext db) : IIctJobRunRepository
{
    public async Task<IReadOnlyList<IctJobRunSummary>> GetLatestByJobAsync(
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(
            """
            SELECT DISTINCT ON (job_name)
                job_name, started_at, duration_ms, outcome
            FROM ict.job_runs
            ORDER BY job_name, started_at DESC
            """,
            jobName: null,
            take: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IctJobRunSummary>> ListRecentAsync(
        string jobName,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);

        return await ExecuteAsync(
            """
            SELECT job_name, started_at, duration_ms, outcome
            FROM ict.job_runs
            WHERE job_name = @jobName
            ORDER BY started_at DESC
            LIMIT @take
            """,
            jobName,
            take,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<IctJobRunSummary>> ExecuteAsync(
        string sql,
        string? jobName,
        int? take,
        CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var conn = db.Database.GetDbConnection();
            var wasClosed = conn.State != ConnectionState.Open;
            if (wasClosed)
            {
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                if (jobName is not null)
                {
                    AddParam(cmd, "jobName", jobName);
                }

                if (take is not null)
                {
                    AddParam(cmd, "take", take.Value);
                }

                var rows = new List<IctJobRunSummary>();
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rows.Add(new IctJobRunSummary(
                        reader.GetString(0),
                        ReadTimestamp(reader, 1),
                        reader.GetInt32(2),
                        reader.GetString(3)));
                }

                return rows;
            }
            finally
            {
                if (wasClosed)
                {
                    await conn.CloseAsync().ConfigureAwait(false);
                }
            }
        }).ConfigureAwait(false);
    }

    private static DateTimeOffset ReadTimestamp(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return default;
        }

        return reader.GetFieldType(ordinal) == typeof(DateTimeOffset)
            ? reader.GetFieldValue<DateTimeOffset>(ordinal)
            : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
