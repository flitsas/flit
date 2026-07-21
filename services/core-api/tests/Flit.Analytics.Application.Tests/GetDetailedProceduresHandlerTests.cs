using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Analytics.Application.Tests;

/// <summary>Tests del reporte detallado (Feature #10813, HU #10815).</summary>
public sealed class GetDetailedProceduresHandlerTests
{
    private readonly IDetailedReportReadRepository _repo = Substitute.For<IDetailedReportReadRepository>();
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 30);
    private static readonly Guid ProcedureType = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HandleAsync_ConFiltrosValidos_DevuelvePagina()
    {
        var summary = new DetailedReportSummaryDto(1,
            [new StatusCountDto("aprobado", 1)],
            [new LabelCountDto("traspasos", 1)],
            [new LabelCountDto("Traspaso", 1)]);
        var row = new DetailedProcedureRowDto(Guid.NewGuid(), "REF-1", "Traspaso", "traspasos", "aprobado",
            "Ana", null, null, "123", "Juan Pérez", true, "Color", false, "CONTADO", "BILATERAL");
        _repo.GetProceduresAsync(Arg.Any<DetailedReportFilter>(), 1, 20, Ct)
            .Returns(new DetailedProceduresPageDto([row], 1, 1, 20, summary));

        var (page, error) = await new GetDetailedProceduresHandler(_repo).HandleAsync(
            new GetDetailedProceduresQuery(Tenant, From, To, null, ProcedureType, null, null, null, null, null, true, null, 1, 20),
            Ct);

        error.Should().BeNull();
        page!.Items.Should().ContainSingle(i => i.ReferenceNumber == "REF-1");
        page.Summary.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_SinFiltrosMinimos_DevuelveMissingFilters()
    {
        var (page, error) = await new GetDetailedProceduresHandler(_repo).HandleAsync(
            new GetDetailedProceduresQuery(Tenant, From, To, null, null, null, null, null, null, null, null, null, 1, 20),
            Ct);

        page.Should().BeNull();
        error.Should().Be("missing_filters");
        await _repo.DidNotReceiveWithAnyArgs().GetProceduresAsync(default!, 0, 0, Ct);
    }

    [Fact]
    public async Task HandleAsync_RangoInvalido_DevuelveInvalidRange()
    {
        var (page, error) = await new GetDetailedProceduresHandler(_repo).HandleAsync(
            new GetDetailedProceduresQuery(Tenant, To, From, null, ProcedureType, null, "aprobado", null, null, null, null, null, 1, 20),
            Ct);

        page.Should().BeNull();
        error.Should().Be("invalid_range");
    }
}
