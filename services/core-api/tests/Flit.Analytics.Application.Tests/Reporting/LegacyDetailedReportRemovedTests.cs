using Xunit;

namespace Flit.Analytics.Application.Tests.Reporting;

/// <summary>
/// Uso de ejemplo: tipos del módulo detailed-report eliminados de Application (HU #11108 AC7).
/// </summary>
public sealed class LegacyDetailedReportRemovedTests
{
    [Fact]
    public void IDetailedReportReadRepository_type_is_absent()
    {
        var appAsm = typeof(Flit.Analytics.Application.Reporting.RequestExportHandler).Assembly;
        var type = appAsm.GetType(
            "Flit.Analytics.Application.Abstractions.IDetailedReportReadRepository",
            throwOnError: false);
        Assert.Null(type);
    }

    [Fact]
    public void GetDetailedProceduresHandler_type_is_absent()
    {
        var appAsm = typeof(Flit.Analytics.Application.Reporting.RequestExportHandler).Assembly;
        var type = appAsm.GetType(
            "Flit.Analytics.Application.Queries.GetDetailedProceduresHandler",
            throwOnError: false);
        Assert.Null(type);
    }
}
