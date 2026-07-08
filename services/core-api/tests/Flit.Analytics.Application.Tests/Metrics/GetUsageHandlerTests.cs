using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Queries.Metrics;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Analytics.Application.Tests.Metrics;

/// <summary>
/// Reportes 2.0 HU-B — <see cref="GetUsageHandler"/>: composición de telemetría (HU-A) con las
/// métricas operacionales; repos sin datos → listas vacías (no null, no error).
/// </summary>
public sealed class GetUsageHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IAnalyticsMetricsReadRepository _repo = Substitute.For<IAnalyticsMetricsReadRepository>();
    private readonly IUsageMetricsReadRepository _usageRepo = Substitute.For<IUsageMetricsReadRepository>();

    [Fact] // §4.1 — from > to → invalid_range sin tocar los repos
    public async Task HandleAsync_FromPosteriorATo_DevuelveInvalidRange()
    {
        var (result, error) = await new GetUsageHandler(_repo, _usageRepo)
            .HandleAsync(new GetUsageQuery(Tenant, To, From), Ct);

        result.Should().BeNull();
        error.Should().Be("invalid_range");
        _repo.ReceivedCalls().Should().BeEmpty();
        _usageRepo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact] // §4.4 — sin datos de telemetría ni operacionales → listas vacías y duraciones null
    public async Task HandleAsync_ReposVacios_DevuelveListasVaciasSinError()
    {
        // Sin configurar los substitutes: devuelven null → el handler coalesce a [].
        var (result, error) = await new GetUsageHandler(_repo, _usageRepo)
            .HandleAsync(new GetUsageQuery(Tenant, From, To), Ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Current.ModuleUsage.Should().NotBeNull().And.BeEmpty();
        result.Current.WizardSteps.Should().NotBeNull().And.BeEmpty();
        result.Current.PeakHours.Should().NotBeNull().And.BeEmpty();
        result.Current.DocumentReplacements.Should().NotBeNull().And.BeEmpty();
        result.Current.ExternalApis.Should().NotBeNull().And.BeEmpty();
        result.Current.AvgWizardDurationMs.Should().BeNull();
        result.Current.MedianWizardDurationMs.Should().BeNull();
    }

    [Fact] // §4.4 — compone las cinco fuentes y la duración del wizard
    public async Task HandleAsync_ComponeTelemetriaYOperacionales()
    {
        _usageRepo.GetModuleUsageAsync(Tenant, From, To, Ct)
            .Returns([new ModuleUsageDto("tramites", 500, 12)]);
        _usageRepo.GetWizardStepMetricsAsync(Tenant, From, To, Ct)
            .Returns([new WizardStepMetricDto("comprador", 100, 80, 20.0, 12000.0, 9000.0)]);
        _usageRepo.GetPeakHoursAsync(Tenant, From, To, Ct)
            .Returns([new PeakHourDto(1, 9, 87)]);
        _usageRepo.GetWizardDurationAsync(Tenant, From, To, Ct)
            .Returns(new WizardDurationDto(1860000.0, 1200000.0));
        _repo.GetDocumentReplacementsAsync(Arg.Any<MetricsFilter>(), Ct)
            .Returns([new DocumentReplacementDto("cedula", 40, 12)]);
        _repo.GetExternalApiMetricsAsync(Arg.Any<MetricsFilter>(), Ct)
            .Returns([new ExternalApiMetricDto("/runt/vin", "outbound", 300, 12, 4.0, 420.5, 900.0)]);

        var (result, error) = await new GetUsageHandler(_repo, _usageRepo)
            .HandleAsync(new GetUsageQuery(Tenant, From, To), Ct);

        error.Should().BeNull();
        result!.Current.ModuleUsage.Should().ContainSingle(m => m.Module == "tramites" && m.Events == 500);
        result.Current.WizardSteps.Should().ContainSingle(s => s.StepKey == "comprador");
        result.Current.PeakHours.Should().ContainSingle(p => p.DayOfWeek == 1 && p.Hour == 9);
        result.Current.DocumentReplacements.Should().ContainSingle(d => d.DocumentTipo == "cedula" && d.Replacements == 12);
        result.Current.ExternalApis.Should().ContainSingle(a => a.Endpoint == "/runt/vin" && a.ErrorRatePct == 4.0);
        result.Current.AvgWizardDurationMs.Should().Be(1860000.0);
        result.Current.MedianWizardDurationMs.Should().Be(1200000.0);
    }

    [Fact] // §4.1 — previous_year: ambas ventanas en ambos repos
    public async Task HandleAsync_PreviousYear_ConsultaAmbasVentanas()
    {
        var (result, error) = await new GetUsageHandler(_repo, _usageRepo)
            .HandleAsync(new GetUsageQuery(Tenant, From, To, CompareWith: "previous_year"), Ct);

        error.Should().BeNull();
        result!.Previous.Should().NotBeNull();
        result.Comparison.Should().Be(
            new ComparisonInfoDto("previous_year", new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30)));
        await _repo.Received(1).GetExternalApiMetricsAsync(new MetricsFilter(Tenant, From, To), Ct);
        await _repo.Received(1).GetExternalApiMetricsAsync(
            new MetricsFilter(Tenant, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30)), Ct);
        await _usageRepo.Received(1).GetModuleUsageAsync(
            Tenant, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30), Ct);
    }

    [Fact] // compareWith desconocido → invalid_compare_with
    public async Task HandleAsync_CompareWithInvalido_DevuelveError()
    {
        var (result, error) = await new GetUsageHandler(_repo, _usageRepo)
            .HandleAsync(new GetUsageQuery(Tenant, From, To, CompareWith: "ayer"), Ct);

        result.Should().BeNull();
        error.Should().Be("invalid_compare_with");
    }

    [Fact] // §4.1 — filtros comunes propagados al repositorio operacional (y tenant intacto)
    public async Task HandleAsync_ConFiltros_PropagaFiltrosAlRepositorio()
    {
        var office = Guid.NewGuid();

        var (_, error) = await new GetUsageHandler(_repo, _usageRepo).HandleAsync(
            new GetUsageQuery(Tenant, From, To, TransitOfficeId: office, Status: "rechazado", Reason: "ilegible"),
            Ct);

        error.Should().BeNull();
        var expected = new MetricsFilter(
            Tenant, From, To, office, Status: "rechazado", Reason: "ilegible");
        await _repo.Received(1).GetDocumentReplacementsAsync(expected, Ct);
        await _repo.Received(1).GetExternalApiMetricsAsync(expected, Ct);
    }
}
