using System.Text.Json;
using Flit.Analytics.Application.Reporting;
using Xunit;

namespace Flit.Analytics.Application.Tests.Reporting;

/// <summary>HU #11111 — Saved queries CRUD + dashboard preferences.</summary>
public sealed class SavedQueryVisibilityTests
{
    [Fact]
    public void Ac1_Private_query_visible_only_to_owner()
    {
        var tenant = Guid.CreateVersion7();
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();

        Assert.True(SavedQueryVisibility.IsVisibleTo(tenant, owner, isShared: false, tenant, owner));
        Assert.False(SavedQueryVisibility.IsVisibleTo(tenant, owner, isShared: false, tenant, other));
    }

    [Fact]
    public void Ac2_Shared_query_visible_to_same_tenant_peer()
    {
        var tenant = Guid.CreateVersion7();
        var owner = Guid.CreateVersion7();
        var peer = Guid.CreateVersion7();

        Assert.True(SavedQueryVisibility.IsVisibleTo(tenant, owner, isShared: true, tenant, peer));
    }

    [Fact]
    public void Ac5_Shared_query_invisible_across_tenants()
    {
        var owner = Guid.CreateVersion7();
        Assert.False(SavedQueryVisibility.IsVisibleTo(
            Guid.CreateVersion7(), owner, isShared: true, Guid.CreateVersion7(), Guid.CreateVersion7()));
    }
}

public sealed class SavedQueriesHandlerTests
{
    [Fact]
    public async Task Ac1_Creates_private_query_for_caller()
    {
        var tenant = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();
        var repo = new FakeSavedQueryRepository();
        var handler = new SavedQueriesHandler(repo);

        var (created, error) = await handler.CreateAsync(
            tenant, user, "Q1", null, "{}", isShared: false, TestContext.Current.CancellationToken);

        Assert.Null(error);
        Assert.NotNull(created);
        Assert.Equal("Q1", created!.Name);
        Assert.False(created.IsShared);
        Assert.Equal(user, repo.LastCreatedOwner);
        Assert.Equal(tenant, repo.LastCreatedTenant);

        var listed = await handler.ListAsync(tenant, user, TestContext.Current.CancellationToken);
        Assert.Contains(listed, q => q.Id == created.Id);

        var peerList = await handler.ListAsync(tenant, Guid.CreateVersion7(), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(peerList, q => q.Id == created.Id);
    }

    [Fact]
    public async Task Ac2_Shared_query_appears_for_peer_same_tenant()
    {
        var tenant = Guid.CreateVersion7();
        var owner = Guid.CreateVersion7();
        var peer = Guid.CreateVersion7();
        var repo = new FakeSavedQueryRepository();
        var handler = new SavedQueriesHandler(repo);

        var (created, _) = await handler.CreateAsync(
            tenant, owner, "Shared", null, "{}", isShared: true, TestContext.Current.CancellationToken);

        var peerList = await handler.ListAsync(tenant, peer, TestContext.Current.CancellationToken);
        Assert.Contains(peerList, q => q.Id == created!.Id);
    }

    [Fact]
    public async Task Ac4_Delete_foreign_query_returns_forbidden()
    {
        var tenant = Guid.CreateVersion7();
        var owner = Guid.CreateVersion7();
        var caller = Guid.CreateVersion7();
        var repo = new FakeSavedQueryRepository();
        var handler = new SavedQueriesHandler(repo);
        var (created, _) = await handler.CreateAsync(
            tenant, owner, "Q", null, "{}", false, TestContext.Current.CancellationToken);

        var err = await handler.DeleteAsync(tenant, caller, created!.Id, TestContext.Current.CancellationToken);
        Assert.Equal("forbidden", err);
        Assert.Equal(0, repo.DeleteCount);
    }

    [Fact]
    public async Task Ac5_List_does_not_return_other_tenant_queries()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var owner = Guid.CreateVersion7();
        var repo = new FakeSavedQueryRepository();
        var handler = new SavedQueriesHandler(repo);
        var (created, _) = await handler.CreateAsync(
            tenantA, owner, "Q", null, "{}", true, TestContext.Current.CancellationToken);

        var otherTenantList = await handler.ListAsync(tenantB, Guid.CreateVersion7(), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(otherTenantList, q => q.Id == created!.Id);
    }
}

public sealed class DashboardPreferencesHandlerTests
{
    [Fact]
    public async Task Ac3_Upsert_creates_then_updates_without_duplicate()
    {
        var tenant = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();
        var repo = new FakeDashboardPreferencesRepository();
        var handler = new DashboardPreferencesHandler(repo);

        var (first, err1) = await handler.UpsertAsync(
            tenant, user, """{"visibleKpis":["totalTramites"],"kpiOrder":["totalTramites"]}""",
            TestContext.Current.CancellationToken);
        Assert.Null(err1);
        Assert.NotNull(first);
        Assert.Equal(1, repo.UpsertCalls);
        Assert.Equal(1, repo.RowCount);

        var (second, err2) = await handler.UpsertAsync(
            tenant, user, """{"visibleKpis":["aprobados"],"kpiOrder":["aprobados"]}""",
            TestContext.Current.CancellationToken);
        Assert.Null(err2);
        Assert.Equal(2, repo.UpsertCalls);
        Assert.Equal(1, repo.RowCount);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(second!.ConfigJson));
        Assert.True(doc.RootElement.TryGetProperty("visibleKpis", out _));
    }
}

public sealed class SavedQueriesPermissionContractTests
{
    [Fact]
    public void Endpoints_require_saved_queries_and_preferences_permissions()
    {
        var source = FindReportingEndpoints();
        Assert.Contains("RequirePermission(\"reporting.saved-queries.write\")", source, StringComparison.Ordinal);
        Assert.Contains("RequirePermission(\"reporting.saved-queries.read\")", source, StringComparison.Ordinal);
        Assert.Contains("RequirePermission(\"reporting.dashboard.preferences\")", source, StringComparison.Ordinal);
        Assert.Contains("Results.Created($\"/api/v1/reporting/saved-queries/{created!.Id}\"", source, StringComparison.Ordinal);
    }

    private static string FindReportingEndpoints()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "services", "core-api", "src", "Flit.Api", "Endpoints", "Reporting", "ReportingEndpoints.cs");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            candidate = Path.Combine(dir.FullName, "src", "Flit.Api", "Endpoints", "Reporting", "ReportingEndpoints.cs");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        var fallback = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Flit.Api", "Endpoints", "Reporting", "ReportingEndpoints.cs"));
        Assert.True(File.Exists(fallback));
        return File.ReadAllText(fallback);
    }
}

internal sealed class FakeSavedQueryRepository : ISavedQueryRepository
{
    private readonly List<(Guid TenantId, Guid UserId, SavedQueryDto Dto)> _items = [];
    public Guid? LastCreatedOwner { get; private set; }
    public Guid? LastCreatedTenant { get; private set; }
    public int DeleteCount { get; private set; }

    public Task<IReadOnlyList<SavedQueryDto>> ListAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var items = _items
            .Where(x => SavedQueryVisibility.IsVisibleTo(x.TenantId, x.UserId, x.Dto.IsShared, tenantId, userId))
            .Select(x => x.Dto)
            .ToList();
        return Task.FromResult<IReadOnlyList<SavedQueryDto>>(items);
    }

    public Task<SavedQueryDto> CreateAsync(
        Guid tenantId, Guid userId, string name, string? description, string filtersJson, bool isShared, CancellationToken ct = default)
    {
        LastCreatedOwner = userId;
        LastCreatedTenant = tenantId;
        var dto = new SavedQueryDto(Guid.CreateVersion7(), name, description, new { }, isShared, DateTimeOffset.UtcNow, null);
        _items.Add((tenantId, userId, dto));
        return Task.FromResult(dto);
    }

    public Task<SavedQueryDto?> UpdateAsync(
        Guid tenantId, Guid userId, Guid id, string name, string? description, string filtersJson, bool isShared, CancellationToken ct = default) =>
        Task.FromResult<SavedQueryDto?>(null);

    public Task<string?> DeleteAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct = default)
    {
        var ownership = _items.FirstOrDefault(x => x.TenantId == tenantId && x.Dto.Id == id);
        if (ownership.Dto is null) return Task.FromResult<string?>("not_found");
        if (ownership.UserId != userId) return Task.FromResult<string?>("forbidden");
        DeleteCount++;
        _items.RemoveAll(x => x.Dto.Id == id);
        return Task.FromResult<string?>(null);
    }

    public Task<(Guid OwnerUserId, Guid TenantId)?> GetOwnershipAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var item = _items.FirstOrDefault(x => x.TenantId == tenantId && x.Dto.Id == id);
        return Task.FromResult<(Guid, Guid)?>(item.Dto is null ? null : (item.UserId, item.TenantId));
    }
}

internal sealed class FakeDashboardPreferencesRepository : IDashboardPreferencesRepository
{
    private readonly Dictionary<(Guid Tenant, Guid User), string> _rows = new();
    public int UpsertCalls { get; private set; }
    public int RowCount => _rows.Count;

    public Task<DashboardPreferencesDto> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        _rows.TryGetValue((tenantId, userId), out var json);
        return Task.FromResult(new DashboardPreferencesDto(JsonSerializer.Deserialize<object>(json ?? "{}")!));
    }

    public Task<DashboardPreferencesDto> UpsertAsync(Guid tenantId, Guid userId, string configJson, CancellationToken ct = default)
    {
        UpsertCalls++;
        _rows[(tenantId, userId)] = configJson;
        return Task.FromResult(new DashboardPreferencesDto(JsonSerializer.Deserialize<object>(configJson)!));
    }
}
