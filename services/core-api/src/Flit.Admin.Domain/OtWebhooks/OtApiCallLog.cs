namespace Flit.Admin.Domain.OtWebhooks;

/// <summary>Entrada de bitácora de llamadas API OT (HU #10216).</summary>
public sealed class OtApiCallLog
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    public string Direction { get; init; } = string.Empty;

    public string Endpoint { get; init; } = string.Empty;

    public string HttpMethod { get; init; } = string.Empty;

    public string PayloadHash { get; init; } = string.Empty;

    public short? ResponseCode { get; init; }

    public int? DurationMs { get; init; }

    public DateTimeOffset CalledAt { get; init; }

    public Guid? CorrelationId { get; init; }
}
