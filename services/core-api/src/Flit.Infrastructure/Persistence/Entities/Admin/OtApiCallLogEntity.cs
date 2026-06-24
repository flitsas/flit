namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>Bitácora de llamadas API OT — <c>admin.ot_api_call_logs</c> (HU #10152 / #10216).</summary>
public sealed class OtApiCallLogEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Direction { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string HttpMethod { get; set; } = string.Empty;

    public string PayloadHash { get; set; } = string.Empty;

    public short? ResponseCode { get; set; }

    public int? DurationMs { get; set; }

    public DateTimeOffset CalledAt { get; set; }

    public Guid? CorrelationId { get; set; }

    public string? ErrorMessage { get; set; }
}
