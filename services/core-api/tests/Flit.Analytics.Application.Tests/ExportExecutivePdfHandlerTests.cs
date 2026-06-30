using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries;
using FluentAssertions;
using Xunit;

namespace Flit.Analytics.Application.Tests;

/// <summary>
/// Uso de ejemplo:
/// var (pdf, error) = await new ExportExecutivePdfHandler(repo, gen).HandleAsync(new ExportExecutivePdfQuery(tenant, from, to), ct);
/// Cubre HU #10246 AC1 (ensambla totales por categoría + Top 5 y genera PDF) y rango inválido.
/// </summary>
public sealed class ExportExecutivePdfHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 30);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeRepo(
        IReadOnlyList<CategoryMetricsDto> categories, IReadOnlyList<TopProducerDto> top) : IAnalyticsReadRepository
    {
        public int RequestedLimit { get; private set; }

        public Task<IReadOnlyList<CategoryMetricsDto>> GetOverviewAsync(Guid? t, DateOnly f, DateOnly to, CancellationToken ct = default) =>
            Task.FromResult(categories);
        public Task<IReadOnlyList<TopProducerDto>> GetTopProducersAsync(Guid? t, DateOnly f, DateOnly to, int limit, CancellationToken ct = default)
        {
            RequestedLimit = limit;
            return Task.FromResult(top);
        }
        public Task<IReadOnlyList<MonthlyTrendPointDto>> GetMonthlyTrendAsync(Guid? t, DateOnly f, DateOnly to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MonthlyTrendPointDto>>(new List<MonthlyTrendPointDto>());
        public Task<ProcedureDetailsPageDto> GetProcedureDetailsAsync(Guid t, DateOnly f, DateOnly to, string? c, string? s, int page, int pageSize, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task ExportProcedureDetailsAsync(Guid t, DateOnly f, DateOnly to, string? c, string? s, Func<ProcedureDetailDto, CancellationToken, Task> onRowAsync, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingPdf : IExecutiveSummaryPdfGenerator
    {
        public ExecutiveSummaryData? Captured { get; private set; }
        public byte[] Generate(ExecutiveSummaryData data)
        {
            Captured = data;
            return [1, 2, 3];
        }
    }

    [Fact] // AC1 — ensambla categorías + Top 5 y devuelve el PDF generado
    public async Task HandleAsync_RangoValido_EnsamblaDatosYGeneraPdf()
    {
        var categories = new List<CategoryMetricsDto> { new("matriculas", 5, new List<StatusCountDto>()) };
        var top = new List<TopProducerDto> { new(Guid.NewGuid(), "Ana", 5, 2, 1) };
        var repo = new FakeRepo(categories, top);
        var pdf = new CapturingPdf();

        var (bytes, error) = await new ExportExecutivePdfHandler(repo, pdf)
            .HandleAsync(new ExportExecutivePdfQuery(Tenant, From, To), Ct);

        error.Should().BeNull();
        bytes.Should().Equal(1, 2, 3);
        repo.RequestedLimit.Should().Be(ExportExecutivePdfHandler.TopLimit);
        pdf.Captured.Should().NotBeNull();
        pdf.Captured!.TenantId.Should().Be(Tenant);
        pdf.Captured.From.Should().Be(From);
        pdf.Captured.Categories.Should().BeEquivalentTo(categories);
        pdf.Captured.TopProducers.Should().BeEquivalentTo(top);
    }

    [Fact] // Edge — rango inválido → invalid_range, no genera PDF
    public async Task HandleAsync_FromPosteriorATo_DevuelveInvalidRange()
    {
        var pdf = new CapturingPdf();

        var (bytes, error) = await new ExportExecutivePdfHandler(new FakeRepo([], []), pdf)
            .HandleAsync(new ExportExecutivePdfQuery(Tenant, To, From), Ct);

        bytes.Should().BeNull();
        error.Should().Be("invalid_range");
        pdf.Captured.Should().BeNull();
    }
}
