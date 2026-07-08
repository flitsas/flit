using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Queries.Metrics;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Analytics.Application.Tests.Metrics;

/// <summary>
/// Reportes 2.0 HU-B — <see cref="GetFunnelHandler"/>: composición del funnel operacional con la
/// telemetría del wizard (HU-A), listas vacías sin telemetría y ventana de comparación.
/// </summary>
public sealed class GetFunnelHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IAnalyticsMetricsReadRepository _repo = Substitute.For<IAnalyticsMetricsReadRepository>();
    private readonly IUsageMetricsReadRepository _usageRepo = Substitute.For<IUsageMetricsReadRepository>();

    public GetFunnelHandlerTests()
    {
        _repo.GetFunnelAsync(Arg.Any<MetricsFilter>(), Arg.Any<CancellationToken>())
            .Returns(MetricsTestData.FunnelCore());
        _usageRepo.GetWizardStepMetricsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    [Fact] // §4.1 — from > to → invalid_range sin tocar los repos
    public async Task HandleAsync_FromPosteriorATo_DevuelveInvalidRange()
    {
        var (result, error) = await new GetFunnelHandler(_repo, _usageRepo)
            .HandleAsync(new GetFunnelQuery(Tenant, To, From), Ct);

        result.Should().BeNull();
        error.Should().Be("invalid_range");
        _repo.ReceivedCalls().Should().BeEmpty();
        _usageRepo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact] // §4.3 — compone states/anulados/rechazadosVigentes con los wizardSteps de la telemetría
    public async Task HandleAsync_ComponeFunnelConTelemetria()
    {
        var steps = new List<WizardStepMetricDto> { new("comprador", 100, 80, 20.0, 12000.0, 9000.0) };
        _usageRepo.GetWizardStepMetricsAsync(Tenant, From, To, Ct).Returns(steps);

        var (result, error) = await new GetFunnelHandler(_repo, _usageRepo)
            .HandleAsync(new GetFunnelQuery(Tenant, From, To), Ct);

        error.Should().BeNull();
        result!.Previous.Should().BeNull();
        result.Comparison.Should().BeNull();
        result.Current.States.Should().HaveCount(4);
        result.Current.Anulados.Should().Be(12);
        result.Current.RechazadosVigentes.Should().Be(18);
        result.Current.WizardSteps.Should().BeSameAs(steps);
    }

    [Fact] // §4.3 — sin telemetría (HU-A sin datos) → wizardSteps = [] (no null, no error)
    public async Task HandleAsync_SinTelemetria_DevuelveListaVacia()
    {
        _usageRepo.GetWizardStepMetricsAsync(Tenant, From, To, Ct)
            .Returns((IReadOnlyList<WizardStepMetricDto>)null!);

        var (result, error) = await new GetFunnelHandler(_repo, _usageRepo)
            .HandleAsync(new GetFunnelQuery(Tenant, From, To), Ct);

        error.Should().BeNull();
        result!.Current.WizardSteps.Should().NotBeNull().And.BeEmpty();
    }

    [Fact] // §4.1 — previous_period consulta AMBAS ventanas en ambos repos
    public async Task HandleAsync_PreviousPeriod_ConsultaVentanaAnteriorEnAmbosRepos()
    {
        var (result, error) = await new GetFunnelHandler(_repo, _usageRepo)
            .HandleAsync(new GetFunnelQuery(Tenant, From, To, CompareWith: "previous_period"), Ct);

        error.Should().BeNull();
        result!.Previous.Should().NotBeNull();
        result.Comparison.Should().Be(
            new ComparisonInfoDto("previous_period", new DateOnly(2026, 5, 2), new DateOnly(2026, 5, 31)));
        await _repo.Received(1).GetFunnelAsync(new MetricsFilter(Tenant, From, To), Ct);
        await _repo.Received(1).GetFunnelAsync(
            new MetricsFilter(Tenant, new DateOnly(2026, 5, 2), new DateOnly(2026, 5, 31)), Ct);
        await _usageRepo.Received(1).GetWizardStepMetricsAsync(
            Tenant, new DateOnly(2026, 5, 2), new DateOnly(2026, 5, 31), Ct);
    }

    [Fact] // compareWith desconocido → invalid_compare_with
    public async Task HandleAsync_CompareWithInvalido_DevuelveError()
    {
        var (result, error) = await new GetFunnelHandler(_repo, _usageRepo)
            .HandleAsync(new GetFunnelQuery(Tenant, From, To, CompareWith: "otro"), Ct);

        result.Should().BeNull();
        error.Should().Be("invalid_compare_with");
    }

    [Fact] // Aislamiento multi-tenant: el tenant viaja intacto a AMBOS repositorios
    public async Task HandleAsync_PasaElTenantCorrectoALosRepositorios()
    {
        await new GetFunnelHandler(_repo, _usageRepo)
            .HandleAsync(new GetFunnelQuery(Tenant, From, To), Ct);

        await _repo.Received(1).GetFunnelAsync(Arg.Is<MetricsFilter>(f => f.TenantId == Tenant), Ct);
        await _usageRepo.Received(1).GetWizardStepMetricsAsync(Tenant, From, To, Ct);
    }
}
