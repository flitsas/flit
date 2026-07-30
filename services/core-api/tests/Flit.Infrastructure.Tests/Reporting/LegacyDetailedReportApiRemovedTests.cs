using System.Reflection;
using Xunit;

namespace Flit.Infrastructure.Tests.Reporting;

/// <summary>
/// Uso de ejemplo: endpoints legado y hub SignalR (HU #11108 AC1/AC4/AC7).
/// </summary>
public sealed class LegacyDetailedReportApiRemovedTests
{
    [Fact]
    public void DetailedReportEndpoints_type_is_absent_from_Api_assembly()
    {
        var apiAsm = Assembly.Load("Flit.Api");
        var type = apiAsm.GetType("Flit.Api.Endpoints.Analytics.DetailedReportEndpoints", throwOnError: false);
        Assert.Null(type);
    }

    [Fact]
    public void ExportJobsHub_type_exists_for_SignalR_wiring()
    {
        var hub = Type.GetType("Flit.Infrastructure.Hubs.ExportJobsHub, Flit.Infrastructure", throwOnError: false);
        Assert.NotNull(hub);
    }

    [Fact]
    public void DetailedReportReadRepository_type_is_absent()
    {
        var infraAsm = typeof(Flit.Infrastructure.Hubs.ExportJobsHub).Assembly;
        var type = infraAsm.GetType(
            "Flit.Infrastructure.Persistence.Repositories.DetailedReportReadRepository",
            throwOnError: false);
        Assert.Null(type);
    }
}
