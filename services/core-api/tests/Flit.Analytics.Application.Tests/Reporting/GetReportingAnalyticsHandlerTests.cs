using Flit.Analytics.Application.Reporting;
using Xunit;

namespace Flit.Analytics.Application.Tests.Reporting;

/// <summary>HU #11110 — consolidado, productividad y SLA jerárquico.</summary>
public sealed class GetConsolidadoHandlerTests
{
    [Fact]
    public async Task Ac1_Defaults_groupBy_to_tipo()
    {
        var repo = new FakeReportingReadRepository();
        var handler = new GetConsolidadoHandler(repo);

        var (result, error) = await handler.HandleAsync(
            Guid.CreateVersion7(),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 6, 30),
            null,
            TestContext.Current.CancellationToken);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("tipo", repo.LastConsolidadoGroupBy);
        Assert.Equal(1, repo.GetConsolidadoCalls);
    }
}

public sealed class ReportingSlaResolverTests
{
    [Fact]
    public void Ac2_Uses_ot_and_procedure_type_specific_hours()
    {
        var ot = Guid.CreateVersion7();
        var configs = new List<ReportingSlaResolver.Config>
        {
            new(null, null, 72),
            new(ot, "traslado", 48),
        };

        var hours = ReportingSlaResolver.ResolveHours(configs, ot, "traslado");
        Assert.Equal((short)48, hours);
    }

    [Fact]
    public void Ac3_Falls_back_to_tenant_global_hours()
    {
        var ot = Guid.CreateVersion7();
        var configs = new List<ReportingSlaResolver.Config>
        {
            new(null, null, 72),
            new(Guid.CreateVersion7(), "otro", 24),
        };

        var hours = ReportingSlaResolver.ResolveHours(configs, ot, "traslado");
        Assert.Equal((short)72, hours);
    }

    [Fact]
    public void Ac6_Returns_null_when_tenant_has_no_configs()
    {
        var hours = ReportingSlaResolver.ResolveHours([], Guid.CreateVersion7(), "traslado");
        Assert.Null(hours);
    }

    [Fact]
    public void Prefers_type_global_over_tenant_global_when_no_ot_match()
    {
        var ot = Guid.CreateVersion7();
        var configs = new List<ReportingSlaResolver.Config>
        {
            new(null, null, 72),
            new(null, "traslado", 36),
        };

        Assert.Equal((short)36, ReportingSlaResolver.ResolveHours(configs, ot, "traslado"));
    }
}

public sealed class GetSlaReportHandlerTests
{
    [Fact]
    public async Task Ac6_Propagates_slaConfigured_false()
    {
        var repo = new FakeReportingReadRepository
        {
            SlaPage = new SlaPageDto([], SlaConfigured: false),
        };
        var handler = new GetSlaReportHandler(repo);

        var (result, error) = await handler.HandleAsync(
            Guid.CreateVersion7(), null, null, TestContext.Current.CancellationToken);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.False(result!.SlaConfigured);
    }

    [Fact]
    public async Task Returns_compliance_page_when_configured()
    {
        var repo = new FakeReportingReadRepository
        {
            SlaPage = new SlaPageDto(
            [
                new SlaRowDto("traslado", "OT-1", 48, 10, 8, 2, 40, 80),
            ],
            SlaConfigured: true),
        };
        var handler = new GetSlaReportHandler(repo);

        var (result, error) = await handler.HandleAsync(
            Guid.CreateVersion7(),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 6, 30),
            TestContext.Current.CancellationToken);

        Assert.Null(error);
        Assert.True(result!.SlaConfigured);
        Assert.Equal(48, result.Items[0].SlaHours);
        Assert.Equal(80, result.Items[0].CompliancePct);
    }
}

public sealed class ReportingAnalyticsPermissionContractTests
{
    [Fact]
    public void Ac4_Consolidado_endpoint_requires_reporting_consolidado()
    {
        var source = FindFile("ReportingEndpoints.cs");
        Assert.Contains("MapGet(\"/consolidado\"", source, StringComparison.Ordinal);
        Assert.Contains("RequirePermission(\"reporting.consolidado\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Ac5_Productivity_endpoint_requires_reporting_productivity()
    {
        var source = FindFile("ReportingEndpoints.cs");
        Assert.Contains("MapGet(\"/productivity\"", source, StringComparison.Ordinal);
        Assert.Contains("RequirePermission(\"reporting.productivity\")", source, StringComparison.Ordinal);
    }

    private static string FindFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "services", "core-api", "src", "Flit.Api", "Endpoints", "Reporting", fileName);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            candidate = Path.Combine(dir.FullName, "src", "Flit.Api", "Endpoints", "Reporting", fileName);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        var fallback = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Flit.Api", "Endpoints", "Reporting", fileName));
        Assert.True(File.Exists(fallback), $"No se encontró {fileName}");
        return File.ReadAllText(fallback);
    }
}
