using Flit.Admin.Application.CompanyDocumentParams;
using Flit.Admin.Domain.CompanyDocumentParams;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.CompanyDocumentParams;

/// <summary>HU #10521 (RF31) — parámetros documentales por compañía gestora.</summary>
public sealed class CompanyDocumentParamsHandlerTests
{
    /// <summary>Repo en memoria con upsert por (tenant, code).</summary>
    private sealed class FakeRepo : ICompanyDocumentParamRepository
    {
        private readonly List<CompanyDocumentParamItem> _items = [];

        public Task<IReadOnlyList<CompanyDocumentParamItem>> ListByTenantAsync(
            Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CompanyDocumentParamItem>>(
                _items.Where(i => i.TenantId == tenantId).OrderBy(i => i.DocumentTypeCode).ToList());

        public Task<CompanyDocumentParamItem> UpsertAsync(
            Guid tenantId, string documentTypeCode, string state, Guid? userId, CancellationToken ct = default)
        {
            var existing = _items.FirstOrDefault(
                i => i.TenantId == tenantId && i.DocumentTypeCode == documentTypeCode);
            if (existing is not null)
            {
                _items.Remove(existing);
                var updated = existing with { State = state };
                _items.Add(updated);
                return Task.FromResult(updated);
            }

            var created = new CompanyDocumentParamItem(Guid.NewGuid(), tenantId, documentTypeCode, state);
            _items.Add(created);
            return Task.FromResult(created);
        }
    }

    [Fact]
    public async Task Upsert_InvalidCode_Returns422()
    {
        var handler = new UpsertCompanyDocumentParamHandler(new FakeRepo());
        var (result, error) = await handler.HandleAsync(
            Guid.NewGuid(), new UpsertCompanyDocumentParamRequest("", "OCULTO"), null,
            TestContext.Current.CancellationToken);

        error.Should().Be("invalid_code");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Upsert_InvalidState_Returns422()
    {
        var handler = new UpsertCompanyDocumentParamHandler(new FakeRepo());
        var (_, error) = await handler.HandleAsync(
            Guid.NewGuid(), new UpsertCompanyDocumentParamRequest("soat", "TALVEZ"), null,
            TestContext.Current.CancellationToken);

        error.Should().Be("invalid_state");
    }

    [Fact]
    public async Task Upsert_NormalizesState_AndPersists()
    {
        var repo = new FakeRepo();
        var upsert = new UpsertCompanyDocumentParamHandler(repo);
        var list = new ListCompanyDocumentParamsHandler(repo);
        var tenant = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        var (created, error) = await upsert.HandleAsync(
            tenant, new UpsertCompanyDocumentParamRequest(" soat ", "obligatorio"), null, ct);

        error.Should().BeNull();
        created!.DocumentTypeCode.Should().Be("soat");
        created.State.Should().Be("OBLIGATORIO");

        var all = await list.HandleAsync(tenant, ct);
        all.Should().ContainSingle(i => i.DocumentTypeCode == "soat" && i.State == "OBLIGATORIO");
    }

    [Fact]
    public async Task Upsert_Twice_UpdatesInPlace()
    {
        var repo = new FakeRepo();
        var upsert = new UpsertCompanyDocumentParamHandler(repo);
        var list = new ListCompanyDocumentParamsHandler(repo);
        var tenant = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        await upsert.HandleAsync(tenant, new UpsertCompanyDocumentParamRequest("cepd", "OCULTO"), null, ct);
        await upsert.HandleAsync(tenant, new UpsertCompanyDocumentParamRequest("cepd", "OPCIONAL"), null, ct);

        var all = await list.HandleAsync(tenant, ct);
        all.Should().ContainSingle(i => i.DocumentTypeCode == "cepd" && i.State == "OPCIONAL");
    }
}
