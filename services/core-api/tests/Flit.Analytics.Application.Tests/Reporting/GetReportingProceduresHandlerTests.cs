using Flit.Analytics.Application.Reporting;
using Xunit;

namespace Flit.Analytics.Application.Tests.Reporting;

/// <summary>HU #11109 — listado procedures: defaults, sort anti-SQLi, rango.</summary>
public sealed class GetReportingProceduresHandlerTests
{
    [Fact]
    public async Task Ac1_Defaults_to_30_day_window_page_1_size_50()
    {
        var repo = new FakeReportingReadRepository();
        var handler = new GetReportingProceduresHandler(repo);

        var (result, error) = await handler.HandleAsync(
            Guid.CreateVersion7(), null, null, null, null, null, null, null,
            null, null, null, null, TestContext.Current.CancellationToken);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal(1, result!.Page);
        Assert.Equal(50, result.PageSize);
        Assert.NotNull(repo.LastFilter);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(today, repo.LastFilter!.To);
        Assert.Equal(today.AddDays(-(ReportingDateRange.DefaultDays - 1)), repo.LastFilter.From);
        Assert.Equal(1, repo.GetProceduresCalls);
    }

    [Fact]
    public async Task Ac6_Rejects_range_wider_than_12_months_without_query()
    {
        var repo = new FakeReportingReadRepository();
        var handler = new GetReportingProceduresHandler(repo);

        var (result, error) = await handler.HandleAsync(
            Guid.CreateVersion7(),
            new DateOnly(2024, 1, 1),
            new DateOnly(2026, 1, 1),
            null, null, null, null, null, null, null, null, null,
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal("date_range_too_wide", error);
        Assert.Equal(0, repo.GetProceduresCalls);
    }

    [Fact]
    public async Task Ac8_Rejects_invalid_sortBy_without_query()
    {
        var repo = new FakeReportingReadRepository();
        var handler = new GetReportingProceduresHandler(repo);

        var (result, error) = await handler.HandleAsync(
            Guid.CreateVersion7(), null, null, null, null, null, null, null,
            "DROP_TABLE", null, null, null, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal("invalid_sort", error);
        Assert.Equal(0, repo.GetProceduresCalls);
    }

    [Fact]
    public async Task Ac4_Maps_elapsed_hours_sort_key_before_query()
    {
        var repo = new FakeReportingReadRepository();
        var handler = new GetReportingProceduresHandler(repo);

        var (_, error) = await handler.HandleAsync(
            Guid.CreateVersion7(), null, null, null, null, null, null, null,
            "elapsed_hours", "asc", null, null, TestContext.Current.CancellationToken);

        Assert.Null(error);
        Assert.Equal("elapsed_hours", repo.LastFilter!.SortBy);
        Assert.Equal("v.elapsed_hours_total", ReportingProceduresSort.MapColumn("elapsed_hours"));
    }
}

public sealed class ReportingProceduresSortTests
{
    [Fact]
    public void Ac4_Maps_elapsed_hours_to_elapsed_hours_total_column()
    {
        Assert.Equal("v.elapsed_hours_total", ReportingProceduresSort.MapColumn("elapsed_hours"));
        Assert.Equal("v.created_at", ReportingProceduresSort.MapColumn(null));
        Assert.Null(ReportingProceduresSort.MapColumn("DROP_TABLE"));
    }
}

public sealed class ReportingTenantAccessTests
{
    [Fact]
    public void Ac2_SuperAdmin_with_tenantId_uses_requested_tenant()
    {
        var tenantA = Guid.CreateVersion7();
        var (tenant, error) = ReportingTenantAccess.Resolve(true, null, tenantA);
        Assert.Null(error);
        Assert.Equal(tenantA, tenant);
    }

    [Fact]
    public void Ac5_Regular_tenant_with_foreign_tenantId_is_forbidden()
    {
        var claim = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var (tenant, error) = ReportingTenantAccess.Resolve(false, claim, other);
        Assert.Equal("forbidden", error);
        Assert.Equal(Guid.Empty, tenant);
    }

    [Fact]
    public void Regular_tenant_without_query_uses_claim()
    {
        var claim = Guid.CreateVersion7();
        var (tenant, error) = ReportingTenantAccess.Resolve(false, claim, null);
        Assert.Null(error);
        Assert.Equal(claim, tenant);
    }
}

public sealed class ReportingAuditSignalsTests
{
    [Fact]
    public void Ac3_HistoryAvailable_true_when_any_role_id_present()
    {
        Assert.True(ReportingAuditSignals.HistoryAvailable([null, Guid.CreateVersion7(), null]));
    }

    [Fact]
    public void Ac7_HistoryAvailable_false_when_all_role_ids_null()
    {
        Assert.False(ReportingAuditSignals.HistoryAvailable([null, null]));
        Assert.False(ReportingAuditSignals.HistoryAvailable(Array.Empty<Guid?>()));
    }
}

internal sealed class FakeReportingReadRepository : IReportingReadRepository
{
    public int GetProceduresCalls { get; private set; }
    public ReportingProceduresFilter? LastFilter { get; private set; }

    public Task<ReportingProceduresPageDto> GetProceduresAsync(
        ReportingProceduresFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        GetProceduresCalls++;
        LastFilter = filter;
        return Task.FromResult(new ReportingProceduresPageDto(
            [], 0, page, pageSize, new ReportingKpisDto(0, 0, 0, 0, null)));
    }

    public Task<ReportingProcedureRowDto?> GetProcedureAsync(
        Guid tenantId, Guid procedureId, CancellationToken ct = default) =>
        Task.FromResult<ReportingProcedureRowDto?>(null);

    public Task<ReportingAuditDto> GetAuditAsync(
        Guid tenantId, Guid procedureId, CancellationToken ct = default) =>
        Task.FromResult(new ReportingAuditDto(procedureId, false, []));

    public Task<ConsolidadoPageDto> GetConsolidadoAsync(
        Guid tenantId, DateOnly from, DateOnly toDate, string groupBy, CancellationToken ct = default) =>
        Task.FromResult(new ConsolidadoPageDto([], 0));

    public Task<ProductivityPageDto> GetProductivityAsync(
        Guid tenantId, DateOnly from, DateOnly toDate, string dimension, CancellationToken ct = default) =>
        Task.FromResult(new ProductivityPageDto([]));

    public Task<SlaPageDto> GetSlaAsync(
        Guid tenantId, DateOnly from, DateOnly toDate, CancellationToken ct = default) =>
        Task.FromResult(new SlaPageDto([]));
}
