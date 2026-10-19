using Flit.Modules.Security.Application.PermissionOverrides;
using Flit.Modules.Security.Domain.PermissionOverrides;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.PermissionOverrides;

/// <summary>HU #13647: logica de ambito y normalizacion de filtros.</summary>
public sealed class QueryPermissionAuditHandlerTests
{
    private static readonly Guid Mine = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private sealed class FakeReader : IPermissionAuditReader
    {
        public PermissionAuditFilter? Last { get; private set; }

        public Task<PermissionAuditPage> QueryAsync(PermissionAuditFilter filter, CancellationToken ct)
        {
            Last = filter;
            return Task.FromResult(new PermissionAuditPage([], 0, filter.Page, filter.PageSize));
        }

        public Task<IReadOnlyList<OverrideGrantInfo>> GetGrantInfoAsync(Guid tenantId, Guid userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<OverrideGrantInfo>>([]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SuperAdmin_sin_tenant_no_restringe()
    {
        var reader = new FakeReader();
        await new QueryPermissionAuditHandler(reader).HandleAsync(Mine, true, null, null, null, null, null, null, null, Ct);
        reader.Last!.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task SuperAdmin_con_tenant_lo_respeta()
    {
        var reader = new FakeReader();
        await new QueryPermissionAuditHandler(reader).HandleAsync(Mine, true, Other, null, null, null, null, null, null, Ct);
        reader.Last!.TenantId.Should().Be(Other);
    }

    [Fact]
    public async Task Admin_sin_tenant_se_limita_al_suyo()
    {
        var reader = new FakeReader();
        await new QueryPermissionAuditHandler(reader).HandleAsync(Mine, false, null, null, null, null, null, null, null, Ct);
        reader.Last!.TenantId.Should().Be(Mine);
    }

    [Fact]
    public async Task Admin_con_otro_tenant_lanza_scope()
    {
        var act = () => new QueryPermissionAuditHandler(new FakeReader())
            .HandleAsync(Mine, false, Other, null, null, null, null, null, null, Ct);
        await act.Should().ThrowAsync<PermissionAuditScopeException>();
    }

    [Theory]
    [InlineData(null, null, 1, 50)]
    [InlineData(0, 0, 1, 1)]
    [InlineData(3, 9999, 3, 200)]
    public async Task Normaliza_pagina_y_tamano(int? page, int? size, int expectedPage, int expectedSize)
    {
        var reader = new FakeReader();
        await new QueryPermissionAuditHandler(reader).HandleAsync(Mine, false, null, null, null, null, null, page, size, Ct);
        (reader.Last!.Page, reader.Last.PageSize).Should().Be((expectedPage, expectedSize));
    }
}
