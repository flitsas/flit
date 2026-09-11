using Flit.Admin.Domain.Ict;

namespace Flit.Admin.Application.Ict;

/// <summary>Catálogo SuperAdmin de jobs ICT + último run del pipeline (HU #12513 AC1/AC2).</summary>
public sealed class GetIctJobCatalogHandler
{
    private readonly IIctJobRunRepository _runs;

    public GetIctJobCatalogHandler(IIctJobRunRepository runs)
    {
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    }

    public async Task<IReadOnlyList<IctJobCatalogItemView>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var latest = await _runs.GetLatestByJobAsync(cancellationToken).ConfigureAwait(false);
        var byName = new Dictionary<string, IctJobRunSummary>(StringComparer.Ordinal);
        foreach (var run in latest)
        {
            byName.TryAdd(run.JobName, run);
        }

        var items = new List<IctJobCatalogItemView>(IctJobCatalog.All.Count);
        foreach (var job in IctJobCatalog.All)
        {
            IctJobLastRunView? lastRun = null;
            if (job.HasPipelineRuns && byName.TryGetValue(job.Key, out var summary))
            {
                lastRun = new IctJobLastRunView(summary.Outcome, summary.StartedAt, summary.DurationMs);
            }

            items.Add(new IctJobCatalogItemView(
                job.Key,
                job.DisplayName,
                IctJobCatalog.Owner,
                job.Types,
                job.HasPipelineRuns,
                job.Notes,
                lastRun));
        }

        return items;
    }
}
