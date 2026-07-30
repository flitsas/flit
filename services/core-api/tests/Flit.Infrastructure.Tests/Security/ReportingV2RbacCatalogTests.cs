using Flit.Infrastructure.Security;
using Xunit;

namespace Flit.Infrastructure.Tests.Security;

/// <summary>
/// Uso de ejemplo: contrato de matriz RBAC Reporting V2 (HU #11105 AC1–AC4).
/// </summary>
public sealed class ReportingV2RbacCatalogTests
{
    [Fact]
    public void Has_exactly_15_reporting_slugs()
    {
        Assert.Equal(ReportingV2RbacCatalog.ExpectedReportingSlugCount, ReportingV2RbacCatalog.ReportingSlugs.Count);
        Assert.Equal(15, ReportingV2RbacCatalog.ReportingSlugNames.Count);
    }

    [Fact]
    public void Contains_all_required_slugs_from_AC1()
    {
        string[] required =
        [
            "reporting.read",
            "reporting.detail",
            "reporting.export",
            "reporting.export.download",
            "reporting.saved-queries.read",
            "reporting.saved-queries.write",
            "reporting.schedules.read",
            "reporting.schedules.write",
            "reporting.alerts.read",
            "reporting.alerts.write",
            "reporting.dashboard.preferences",
            "reporting.audit",
            "reporting.consolidado",
            "reporting.productivity",
            "reporting.global",
        ];

        Assert.Equal(required.OrderBy(x => x, StringComparer.Ordinal),
            ReportingV2RbacCatalog.ReportingSlugNames.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Module_code_and_name_match_AC2()
    {
        Assert.Equal("reportes-v2", ReportingV2RbacCatalog.ModuleCode);
        Assert.Equal("Reportería Transaccional V2", ReportingV2RbacCatalog.ModuleName);
    }

    [Fact]
    public void Legacy_detailed_report_slugs_are_defined_for_AC3()
    {
        Assert.Contains(ReportingV2RbacCatalog.LegacyDetailedReportSlugs, s => s.Slug == "detailed-report.read");
        Assert.Contains(ReportingV2RbacCatalog.LegacyDetailedReportSlugs, s => s.Slug == "detailed-report.export");
        Assert.All(ReportingV2RbacCatalog.LegacyDetailedReportSlugs,
            s => Assert.StartsWith("detailed-report.", s.Slug, StringComparison.Ordinal));
    }

    [Fact]
    public void Reporting_slugs_are_unique_idempotent_contract()
    {
        var names = ReportingV2RbacCatalog.ReportingSlugNames;
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Reporting_global_has_global_scope()
    {
        var global = Assert.Single(ReportingV2RbacCatalog.ReportingSlugs, s => s.Slug == "reporting.global");
        Assert.Equal("global", global.Scope);
    }

    [Fact]
    public void Sql_seed_script_is_embedded_and_idempotent()
    {
        var sql = Flit.Infrastructure.Persistence.Sql.EmbeddedDdl.LoadUp("47-HU11105-reporting-v2-rbac-seed.sql");
        Assert.Contains("reportes-v2", sql, StringComparison.Ordinal);
        Assert.Contains("Reportería Transaccional V2", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (slug)", sql, StringComparison.Ordinal);
        Assert.Contains("detailed-report.read", sql, StringComparison.Ordinal);
        Assert.Contains("is_active = false", sql, StringComparison.Ordinal);
        foreach (var slug in ReportingV2RbacCatalog.ReportingSlugNames)
            Assert.Contains(slug, sql, StringComparison.Ordinal);
    }
}
