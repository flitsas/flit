using Flit.Api.RateLimiting;
using Flit.Modules.Security.Application.Auth.BulkInvitation;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.RateLimiting;

/// <summary>
/// HU #14424 AC4 — límite de dryRun por usuario (frena enumeración de correos).
/// Uso de ejemplo: <c>limiter.TryAcquire(userId)</c> → false tras superar el tope.
/// </summary>
public sealed class BulkInvitationDryRunLimiterTests
{
    [Fact]
    public void TryAcquire_WithinLimit_ReturnsTrue()
    {
        using var limiter = new BulkInvitationDryRunLimiter(new BulkInvitationOptions { DryRunRequestsPerUserPerMinute = 3 });
        var user = Guid.NewGuid();

        limiter.TryAcquire(user).Should().BeTrue();
        limiter.TryAcquire(user).Should().BeTrue();
        limiter.TryAcquire(user).Should().BeTrue();
    }

    [Fact]
    public void TryAcquire_ExceedsLimit_ReturnsFalse()
    {
        using var limiter = new BulkInvitationDryRunLimiter(new BulkInvitationOptions { DryRunRequestsPerUserPerMinute = 2 });
        var user = Guid.NewGuid();

        limiter.TryAcquire(user).Should().BeTrue();
        limiter.TryAcquire(user).Should().BeTrue();
        limiter.TryAcquire(user).Should().BeFalse();
    }

    [Fact]
    public void TryAcquire_DifferentUsers_HaveIndependentQuotas()
    {
        using var limiter = new BulkInvitationDryRunLimiter(new BulkInvitationOptions { DryRunRequestsPerUserPerMinute = 1 });
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        limiter.TryAcquire(a).Should().BeTrue();
        limiter.TryAcquire(a).Should().BeFalse();
        limiter.TryAcquire(b).Should().BeTrue();
    }
}
