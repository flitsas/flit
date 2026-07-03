using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;

public sealed class GetStatusHistoryHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly GetStatusHistoryHandler _handler;

    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    public GetStatusHistoryHandlerTests()
    {
        _handler = new GetStatusHistoryHandler(_repo);
    }

    private static ProcedureInstanceStatusHistoryEntry Entry(string to, DateTimeOffset at, string? name = null) =>
        new(Guid.NewGuid(), "borrador", to, at, name is null ? null : Guid.NewGuid(), name, null);

    [Fact]
    public async Task InstanceNotFoundOrOtherTenant_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, Arg.Any<int>(), Arg.Any<int>(), ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>, int)?)null);

        var (result, error) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task MapsItemsAndTotals()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var newer = Entry("preparado", now, "Ana Gestora");
        var older = Entry("borrador", now.AddMinutes(-5));
        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>)[newer, older], 7));

        var (result, error) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Total.Should().Be(7);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.Items.Should().HaveCount(2);
        result.Items[0].ToStatus.Should().Be("preparado");
        result.Items[0].ChangedByName.Should().Be("Ana Gestora");
        result.Items[1].ChangedByName.Should().BeNull();
    }

    [Theory]
    [InlineData(0, 0, 0, 20)]     // defaults: page<1 → 1, pageSize<1 → 20
    [InlineData(3, 10, 20, 10)]   // skip = (page-1)*pageSize
    [InlineData(1, 500, 0, 100)]  // cap de pageSize en 100
    public async Task NormalizesPagination(int page, int pageSize, int expectedSkip, int expectedTake)
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, expectedSkip, expectedTake, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>)[], 0));

        var (result, error) = await _handler.HandleAsync(InstanceId, TenantId, page, pageSize, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        await _repo.Received(1).GetStatusHistoryPageAsync(InstanceId, TenantId, expectedSkip, expectedTake, ct);
    }
}
