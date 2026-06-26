using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Analytics.Application.Tests;

/// <summary>
/// Uso de ejemplo:
/// var handler = new GetTopProducersHandler(repo);
/// var (items, error) = await handler.HandleAsync(new GetTopProducersQuery(tenant, from, to, 5), ct);
/// Cubre HU #10243 AC3 (Top productividad por submitted desc) y AC4 (rango inválido).
/// </summary>
public sealed class GetTopProducersHandlerTests
{
    private readonly IAnalyticsReadRepository _repo = Substitute.For<IAnalyticsReadRepository>();
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact] // AC3 — happy path: devuelve el ranking del repo con el límite solicitado
    public async Task HandleAsync_RangoValido_DevuelveItemsConLimite()
    {
        var top = new List<TopProducerDto> { new(Guid.NewGuid(), "Ana", 10, 4, 1) };
        _repo.GetTopProducersAsync(Tenant, From, To, 5, Ct).Returns(top);

        var (items, error) = await new GetTopProducersHandler(_repo)
            .HandleAsync(new GetTopProducersQuery(Tenant, From, To, 5), Ct);

        error.Should().BeNull();
        items.Should().ContainSingle(i => i.DisplayName == "Ana" && i.SubmittedCount == 10);
    }

    [Fact] // Edge: limit <= 0 cae al default (5)
    public async Task HandleAsync_LimitCero_UsaDefault5()
    {
        _repo.GetTopProducersAsync(Tenant, From, To, GetTopProducersHandler.DefaultLimit, Ct)
            .Returns(new List<TopProducerDto>());

        var (_, error) = await new GetTopProducersHandler(_repo)
            .HandleAsync(new GetTopProducersQuery(Tenant, From, To, 0), Ct);

        error.Should().BeNull();
        await _repo.Received(1).GetTopProducersAsync(
            Tenant, From, To, GetTopProducersHandler.DefaultLimit, Ct);
    }

    [Fact] // Edge: limit por encima del tope se acota a MaxLimit
    public async Task HandleAsync_LimitExcesivo_SeAcotaAlMax()
    {
        _repo.GetTopProducersAsync(Tenant, From, To, GetTopProducersHandler.MaxLimit, Ct)
            .Returns(new List<TopProducerDto>());

        await new GetTopProducersHandler(_repo)
            .HandleAsync(new GetTopProducersQuery(Tenant, From, To, 9999), Ct);

        await _repo.Received(1).GetTopProducersAsync(
            Tenant, From, To, GetTopProducersHandler.MaxLimit, Ct);
    }

    [Fact] // AC4 — edge case: from posterior a to → invalid_range y no consulta el repo
    public async Task HandleAsync_FromPosteriorATo_DevuelveInvalidRange()
    {
        var (items, error) = await new GetTopProducersHandler(_repo)
            .HandleAsync(new GetTopProducersQuery(Tenant, To, From, 5), Ct);

        items.Should().BeNull();
        error.Should().Be("invalid_range");
        _repo.ReceivedCalls().Should().BeEmpty();
    }
}
