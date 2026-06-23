namespace Flit.Admin.Domain.OtWebhooks;

/// <summary>Filtros de consulta de bitácora API OT (HU #10216 AC4).</summary>
public sealed class OtApiCallLogFilter
{
    public string? Direction { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
