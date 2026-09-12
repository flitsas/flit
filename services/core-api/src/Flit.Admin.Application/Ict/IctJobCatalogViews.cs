namespace Flit.Admin.Application.Ict;

public sealed record IctJobLastRunView(string Outcome, DateTimeOffset StartedAt, int DurationMs);

public sealed record IctJobCatalogItemView(
    string Key,
    string DisplayName,
    string Owner,
    IReadOnlyList<string> Types,
    bool HasPipelineRuns,
    string? Notes,
    IctJobLastRunView? LastRun);

public sealed record IctJobRunListItemView(DateTimeOffset StartedAt, int DurationMs, string Outcome);
