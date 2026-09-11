using Flit.Admin.Domain.Ict;

namespace Flit.Admin.Application.Ict;

/// <summary>Últimas corridas de un job de pipeline (take acotado, sin PII). HU #12513 AC2.</summary>
public sealed class GetIctJobRunsHandler
{
    public const int DefaultTake = 20;
    public const int MaxTake = 50;

    private readonly IIctJobRunRepository _runs;

    public GetIctJobRunsHandler(IIctJobRunRepository runs)
    {
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    }

    public async Task<GetIctJobRunsResult> HandleAsync(
        string? jobKey,
        int? take,
        CancellationToken cancellationToken = default)
    {
        var definition = IctJobCatalog.Find(jobKey);
        if (definition is null)
        {
            return GetIctJobRunsResult.NotFound();
        }

        if (!definition.HasPipelineRuns)
        {
            return GetIctJobRunsResult.Ok([]);
        }

        var clamped = ClampTake(take);
        var rows = await _runs
            .ListRecentAsync(definition.Key, clamped, cancellationToken)
            .ConfigureAwait(false);

        var items = rows
            .Select(r => new IctJobRunListItemView(r.StartedAt, r.DurationMs, r.Outcome))
            .ToArray();

        return GetIctJobRunsResult.Ok(items);
    }

    private static int ClampTake(int? take)
    {
        if (take is null or < 1)
        {
            return DefaultTake;
        }

        return take.Value > MaxTake ? MaxTake : take.Value;
    }
}

public sealed record GetIctJobRunsResult(bool Exists, IReadOnlyList<IctJobRunListItemView> Items)
{
    public static GetIctJobRunsResult NotFound() => new(false, []);

    public static GetIctJobRunsResult Ok(IReadOnlyList<IctJobRunListItemView> items) => new(true, items);
}
