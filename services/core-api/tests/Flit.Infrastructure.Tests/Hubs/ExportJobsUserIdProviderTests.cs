using System.Security.Claims;
using Flit.Infrastructure.Hubs;
using Xunit;

namespace Flit.Infrastructure.Tests.Hubs;

/// <summary>
/// Uso de ejemplo: resolución de UserId desde JWT para Clients.User (HU #11107 AC1).
/// </summary>
public sealed class ExportJobsUserIdProviderTests
{
    [Fact]
    public void Prefers_sub_claim()
    {
        var userId = Guid.CreateVersion7().ToString("D");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", userId),
            new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString("D")),
        ], "test"));

        Assert.Equal(userId, ExportJobsUserIdProvider.ResolveUserId(principal));
    }

    [Fact]
    public void Falls_back_to_name_identifier()
    {
        var userId = Guid.CreateVersion7().ToString("D");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
        ], "test"));

        Assert.Equal(userId, ExportJobsUserIdProvider.ResolveUserId(principal));
    }

    [Fact]
    public void Returns_null_for_non_guid()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "not-a-guid"),
        ], "test"));

        Assert.Null(ExportJobsUserIdProvider.ResolveUserId(principal));
    }

    [Fact]
    public void Returns_null_when_unauthenticated()
    {
        Assert.Null(ExportJobsUserIdProvider.ResolveUserId(null));
    }
}
