namespace Flit.Admin.Domain.Ict;

/// <summary>
/// Lectura de <c>ict.job_runs</c> (HU #12513). Schema de core-ict, sin RLS.
/// Nunca selecciona error_message ni columnas de payload.
/// </summary>
public interface IIctJobRunRepository
{
    Task<IReadOnlyList<IctJobRunSummary>> GetLatestByJobAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IctJobRunSummary>> ListRecentAsync(
        string jobName,
        int take,
        CancellationToken cancellationToken = default);
}
