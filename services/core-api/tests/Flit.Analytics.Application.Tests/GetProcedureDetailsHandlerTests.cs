using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Analytics.Application.Tests;

/// <summary>
/// Uso de ejemplo:
/// var handler = new GetProcedureDetailsHandler(repo);
/// var (page, error) = await handler.HandleAsync(new GetProcedureDetailsQuery(tenant, from, to, "traspasos", "submitted", 1, 20), ct);
/// Cubre HU #10244 AC1 (listado paginado con totalCount), AC3 (sin resultados) y rango inválido.
/// </summary>
public sealed class GetProcedureDetailsHandlerTests
{
    private readonly IAnalyticsReadRepository _repo = Substitute.For<IAnalyticsReadRepository>();
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact] // AC1 — happy path: devuelve la página y el total del repo; normaliza categoría a minúsculas
    public async Task HandleAsync_ConFiltros_DevuelvePaginaYNormalizaCategoria()
    {
        var item = new ProcedureDetailDto(Guid.NewGuid(), "REF-1", "Traspaso Estándar", "traspasos",
            "submitted", "Ana", DateTimeOffset.UtcNow, null);
        _repo.GetProcedureDetailsAsync(Tenant, From, To, "traspasos", "submitted", 1, 20, Ct)
            .Returns(new ProcedureDetailsPageDto(new List<ProcedureDetailDto> { item }, 1, 1, 20));

        var (page, error) = await new GetProcedureDetailsHandler(_repo)
            .HandleAsync(new GetProcedureDetailsQuery(Tenant, From, To, "TRASPASOS", "submitted", 1, 20), Ct);

        error.Should().BeNull();
        page.Should().NotBeNull();
        page!.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle(i => i.ReferenceNumber == "REF-1" && i.Category == "traspasos");
        await _repo.Received(1).GetProcedureDetailsAsync(Tenant, From, To, "traspasos", "submitted", 1, 20, Ct);
    }

    [Fact] // AC3 — sin resultados: items vacío y totalCount 0
    public async Task HandleAsync_SinResultados_DevuelveVacioYTotalCero()
    {
        _repo.GetProcedureDetailsAsync(Tenant, From, To, null, null, 1, 20, Ct)
            .Returns(new ProcedureDetailsPageDto(new List<ProcedureDetailDto>(), 0, 1, 20));

        var (page, error) = await new GetProcedureDetailsHandler(_repo)
            .HandleAsync(new GetProcedureDetailsQuery(Tenant, From, To, null, "  ", 1, 0), Ct);

        error.Should().BeNull();
        page!.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
        // pageSize 0 → default 20; status en blanco → null (sin filtro)
        await _repo.Received(1).GetProcedureDetailsAsync(
            Tenant, From, To, null, null, 1, GetProcedureDetailsHandler.DefaultPageSize, Ct);
    }

    [Fact] // Edge: pageSize excesivo se acota; page <= 0 → 1
    public async Task HandleAsync_PaginacionFueraDeRango_SeAcota()
    {
        _repo.GetProcedureDetailsAsync(Tenant, From, To, null, null, 1, GetProcedureDetailsHandler.MaxPageSize, Ct)
            .Returns(new ProcedureDetailsPageDto(new List<ProcedureDetailDto>(), 0, 1, GetProcedureDetailsHandler.MaxPageSize));

        await new GetProcedureDetailsHandler(_repo)
            .HandleAsync(new GetProcedureDetailsQuery(Tenant, From, To, null, null, 0, 9999), Ct);

        await _repo.Received(1).GetProcedureDetailsAsync(
            Tenant, From, To, null, null, 1, GetProcedureDetailsHandler.MaxPageSize, Ct);
    }

    [Fact] // Rango inválido → invalid_range, sin consultar el repo
    public async Task HandleAsync_FromPosteriorATo_DevuelveInvalidRange()
    {
        var (page, error) = await new GetProcedureDetailsHandler(_repo)
            .HandleAsync(new GetProcedureDetailsQuery(Tenant, To, From, null, null, 1, 20), Ct);

        page.Should().BeNull();
        error.Should().Be("invalid_range");
        _repo.ReceivedCalls().Should().BeEmpty();
    }
}
