using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Queries.Metrics;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Analytics.Application.Tests.Metrics;

/// <summary>
/// Reportes 2.0 HU-B — <see cref="GetLiveOverviewHandler"/>: stuckDays default/validación,
/// estampado de generatedAt y propagación del tenant al repositorio.
/// </summary>
public sealed class GetLiveOverviewHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IAnalyticsMetricsReadRepository _repo = Substitute.For<IAnalyticsMetricsReadRepository>();

    public GetLiveOverviewHandlerTests()
    {
        _repo.GetLiveOverviewAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MetricsTestData.LiveOverview());
    }

    [Fact] // §4.5 — sin stuckDays → default 7 y datos del repo con generatedAt estampado
    public async Task HandleAsync_SinStuckDays_UsaDefaultSieteYEstampaGeneratedAt()
    {
        var before = DateTimeOffset.UtcNow;

        var (result, error) = await new GetLiveOverviewHandler(_repo)
            .HandleAsync(new GetLiveOverviewQuery(Tenant), Ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.GeneratedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTimeOffset.UtcNow);
        result.Today.Creados.Should().Be(14);
        result.StuckCount.Should().Be(7);
        result.PendingIdentityValidations.Should().Be(3);
        result.IntegrationsLastHour.Calls.Should().Be(25);
        result.LastActivityAt.Should().Be(DateTimeOffset.Parse("2026-07-07T13:59:01Z"));
        await _repo.Received(1).GetLiveOverviewAsync(Tenant, 7, Ct);
    }

    [Fact] // stuckDays explícito en rango → se propaga tal cual
    public async Task HandleAsync_StuckDaysExplicito_SePropagaAlRepositorio()
    {
        var (_, error) = await new GetLiveOverviewHandler(_repo)
            .HandleAsync(new GetLiveOverviewQuery(Tenant, StuckDays: 30), Ct);

        error.Should().BeNull();
        await _repo.Received(1).GetLiveOverviewAsync(Tenant, 30, Ct);
    }

    [Theory] // stuckDays fuera de 1..90 → invalid_stuck_days sin tocar el repo
    [InlineData(0)]
    [InlineData(91)]
    public async Task HandleAsync_StuckDaysFueraDeRango_DevuelveError(int stuckDays)
    {
        var (result, error) = await new GetLiveOverviewHandler(_repo)
            .HandleAsync(new GetLiveOverviewQuery(Tenant, stuckDays), Ct);

        result.Should().BeNull();
        error.Should().Be("invalid_stuck_days");
        _repo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact] // Aislamiento multi-tenant: el tenant resuelto viaja intacto al repositorio
    public async Task HandleAsync_PasaElTenantCorrectoAlRepositorio()
    {
        var otherTenant = Guid.Parse("99999999-9999-9999-9999-999999999999");

        await new GetLiveOverviewHandler(_repo)
            .HandleAsync(new GetLiveOverviewQuery(otherTenant), Ct);

        await _repo.Received(1).GetLiveOverviewAsync(otherTenant, 7, Ct);
        await _repo.DidNotReceive().GetLiveOverviewAsync(
            Arg.Is<Guid>(t => t != otherTenant), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
