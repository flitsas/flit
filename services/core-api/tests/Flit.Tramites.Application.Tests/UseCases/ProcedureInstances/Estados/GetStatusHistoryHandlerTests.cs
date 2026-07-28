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

    [Fact] // Migración V1→V2: el historial muestra el usuario REAL de V1, no el sistema "Migración V1".
    public async Task EventoMigracion_MuestraUsuarioRealDeV1()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        // changed_by resuelve a "Migración V1", pero el metadata conserva el actor real de V1.
        var migrado = new ProcedureInstanceStatusHistoryEntry(
            Guid.NewGuid(), "entregado", "aprobado", now, Guid.NewGuid(), "Migración V1", "Aprobado en RUNT")
        {
            Metadata = """{"origen":"migration_v1","usuario":"DIANA CHACON","usuario_rol":"g_escrituras"}""",
        };
        // Evento nativo con metadata.usuario pero SIN 'origen=migration_v1': NO se altera.
        var nativo = new ProcedureInstanceStatusHistoryEntry(
            Guid.NewGuid(), "borrador", "preparado", now.AddMinutes(-5), Guid.NewGuid(), "Ana Gestora", null)
        {
            Metadata = """{"usuario":"Sistema Interno"}""",
        };
        _repo.GetStatusHistoryPageAsync(InstanceId, TenantId, 0, 20, ct)
            .Returns(((IReadOnlyList<ProcedureInstanceStatusHistoryEntry>)[migrado, nativo], 2));

        var (result, _) = await _handler.HandleAsync(InstanceId, TenantId, 1, 20, ct);

        result!.Items[0].ChangedByName.Should().Be("DIANA CHACON");  // usuario real, no "Migración V1"
        result.Items[0].ChangedByUserId.Should().NotBeNull();        // se conserva la trazabilidad técnica
        result.Items[1].ChangedByName.Should().Be("Ana Gestora");    // nativo intacto
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
