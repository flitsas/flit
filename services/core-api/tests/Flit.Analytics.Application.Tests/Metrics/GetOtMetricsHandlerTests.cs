using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Queries.Metrics;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Analytics.Application.Tests.Metrics;

/// <summary>
/// Reportes 2.0 HU-B — <see cref="GetOtMetricsHandler"/>: validación de rango/compareWith/stuckDays,
/// cálculo de la ventana de comparación (§4.1) y propagación de filtros y tenant al repositorio.
/// </summary>
public sealed class GetOtMetricsHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly From = new(2026, 6, 8);
    private static readonly DateOnly To = new(2026, 6, 14);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IAnalyticsMetricsReadRepository _repo = Substitute.For<IAnalyticsMetricsReadRepository>();

    public GetOtMetricsHandlerTests()
    {
        _repo.GetOtMetricsAsync(Arg.Any<MetricsFilter>(), Arg.Any<CancellationToken>())
            .Returns(MetricsTestData.OtMetrics());
    }

    [Fact] // §4.1 — from > to → invalid_range sin tocar el repo
    public async Task HandleAsync_FromPosteriorATo_DevuelveInvalidRange()
    {
        var (result, error) = await new GetOtMetricsHandler(_repo)
            .HandleAsync(new GetOtMetricsQuery(Tenant, To, From), Ct);

        result.Should().BeNull();
        error.Should().Be("invalid_range");
        _repo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact] // Sin compareWith → previous/comparison null y UNA sola consulta con stuckDays default 7
    public async Task HandleAsync_SinCompareWith_PreviousNull()
    {
        var (result, error) = await new GetOtMetricsHandler(_repo)
            .HandleAsync(new GetOtMetricsQuery(Tenant, From, To), Ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Previous.Should().BeNull();
        result.Comparison.Should().BeNull();
        result.Current.Should().Be(MetricsTestData.OtMetrics());
        await _repo.Received(1).GetOtMetricsAsync(
            new MetricsFilter(Tenant, From, To, StuckDays: 7), Ct);
    }

    [Fact] // §4.1 — previous_period de 7 días: prevTo = from-1, prevFrom = prevTo-(len-1)
    public async Task HandleAsync_PreviousPeriodSieteDias_CalculaVentanaInmediatamenteAnterior()
    {
        var (result, error) = await new GetOtMetricsHandler(_repo)
            .HandleAsync(new GetOtMetricsQuery(Tenant, From, To, CompareWith: "previous_period"), Ct);

        error.Should().BeNull();
        result!.Previous.Should().NotBeNull();
        result.Comparison.Should().Be(
            new ComparisonInfoDto("previous_period", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 7)));
        await _repo.Received(1).GetOtMetricsAsync(
            new MetricsFilter(Tenant, From, To, StuckDays: 7), Ct);
        await _repo.Received(1).GetOtMetricsAsync(
            new MetricsFilter(Tenant, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 7), StuckDays: 7), Ct);
    }

    [Fact] // §4.1 — previous_year: mismas fechas un año atrás
    public async Task HandleAsync_PreviousYear_CalculaMismasFechasUnAnioAtras()
    {
        var (result, error) = await new GetOtMetricsHandler(_repo)
            .HandleAsync(new GetOtMetricsQuery(Tenant, From, To, CompareWith: "previous_year"), Ct);

        error.Should().BeNull();
        result!.Comparison.Should().Be(
            new ComparisonInfoDto("previous_year", new DateOnly(2025, 6, 8), new DateOnly(2025, 6, 14)));
        await _repo.Received(1).GetOtMetricsAsync(
            new MetricsFilter(Tenant, new DateOnly(2025, 6, 8), new DateOnly(2025, 6, 14), StuckDays: 7), Ct);
    }

    [Fact] // compareWith desconocido → invalid_compare_with (400 en el endpoint)
    public async Task HandleAsync_CompareWithInvalido_DevuelveError()
    {
        var (result, error) = await new GetOtMetricsHandler(_repo)
            .HandleAsync(new GetOtMetricsQuery(Tenant, From, To, CompareWith: "last_week"), Ct);

        result.Should().BeNull();
        error.Should().Be("invalid_compare_with");
        _repo.ReceivedCalls().Should().BeEmpty();
    }

    [Theory] // stuckDays fuera de 1..90 → invalid_stuck_days (400 en el endpoint)
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(91)]
    public async Task HandleAsync_StuckDaysFueraDeRango_DevuelveError(int stuckDays)
    {
        var (result, error) = await new GetOtMetricsHandler(_repo)
            .HandleAsync(new GetOtMetricsQuery(Tenant, From, To, StuckDays: stuckDays), Ct);

        result.Should().BeNull();
        error.Should().Be("invalid_stuck_days");
        _repo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact] // §4.1 — los filtros comunes (y el tenant) se propagan tal cual al repositorio
    public async Task HandleAsync_ConFiltros_PropagaFiltrosYTenantAlRepositorio()
    {
        var office = Guid.NewGuid();
        var ptype = Guid.NewGuid();
        var operatorId = Guid.NewGuid();

        var (_, error) = await new GetOtMetricsHandler(_repo).HandleAsync(
            new GetOtMetricsQuery(
                Tenant, From, To, office, ptype, operatorId,
                Status: "entregado", Reason: "ilegible", StuckDays: 30),
            Ct);

        error.Should().BeNull();
        await _repo.Received(1).GetOtMetricsAsync(
            new MetricsFilter(Tenant, From, To, office, ptype, operatorId, "entregado", "ilegible", 30), Ct);
    }

    [Fact] // Aislamiento multi-tenant a nivel de contrato: el tenant resuelto viaja intacto al repo
    public async Task HandleAsync_PasaElTenantCorrectoAlRepositorio()
    {
        var otherTenant = Guid.Parse("99999999-9999-9999-9999-999999999999");

        await new GetOtMetricsHandler(_repo)
            .HandleAsync(new GetOtMetricsQuery(otherTenant, From, To), Ct);

        await _repo.Received(1).GetOtMetricsAsync(
            Arg.Is<MetricsFilter>(f => f.TenantId == otherTenant), Ct);
        await _repo.DidNotReceive().GetOtMetricsAsync(
            Arg.Is<MetricsFilter>(f => f.TenantId != otherTenant), Arg.Any<CancellationToken>());
    }
}
