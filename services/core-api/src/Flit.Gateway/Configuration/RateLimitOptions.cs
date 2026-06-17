namespace Flit.Gateway.Configuration;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    public int PerIpPermitsPerMinute { get; init; } = 600;
    public int PerUserPermitsPerMinute { get; init; } = 1200;
    public int LoginEndpointPermitsPerMinute { get; init; } = 10;
}
