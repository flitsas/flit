using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Analytics.Application.Tests;

/// <summary>
/// Uso de ejemplo:
/// var handler = new GetAnalyticsOverviewHandler(repo);
/// var (dto, error) = await handler.HandleAsync(new GetAnalyticsOverviewQuery(tenant, from, to), ct);
/// Cubre HU #10243 AC1 (overview por tenant) y AC4 (rango inválido → invalid_range).
/// </summary>
public sealed class GetAnalyticsOverviewHandlerTests
{
    private readonly IAnalyticsReadRepository _repo = Substitute.For<IAnalyticsReadRepository>();
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact] // AC1 — happy path: arma el DTO con el tenant/rango y las categorías del repo
    public async Task HandleAsync_RangoValido_DevuelveOverviewConTenantYCategorias()
    {
        var categorias = new List<CategoryMetricsDto>
        {
            new("matriculas", 3, new List<StatusCountDto> { new("submitted", 3) }),
        };
        _repo.GetOverviewAsync(Tenant, From, To, Ct).Returns(categorias);

        var (dto, error) = await new GetAnalyticsOverviewHandler(_repo)
            .HandleAsync(new GetAnalyticsOverviewQuery(Tenant, From, To), Ct);

        error.Should().BeNull();
        dto.Should().NotBeNull();
        dto!.TenantId.Should().Be(Tenant);
        dto.From.Should().Be(From);
        dto.To.Should().Be(To);
        dto.Categories.Should().ContainSingle(c => c.Category == "matriculas" && c.Total == 3);
    }

    [Fact] // AC4 — edge case: from posterior a to → invalid_range y no consulta el repo
    public async Task HandleAsync_FromPosteriorATo_DevuelveInvalidRange()
    {
        var (dto, error) = await new GetAnalyticsOverviewHandler(_repo)
            .HandleAsync(new GetAnalyticsOverviewQuery(Tenant, To, From), Ct);

        dto.Should().BeNull();
        error.Should().Be("invalid_range");
        _repo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact] // Contrato: mismo día (from == to) es rango válido
    public async Task HandleAsync_MismoDia_EsValido()
    {
        _repo.GetOverviewAsync(Tenant, From, From, Ct).Returns(new List<CategoryMetricsDto>());

        var (dto, error) = await new GetAnalyticsOverviewHandler(_repo)
            .HandleAsync(new GetAnalyticsOverviewQuery(Tenant, From, From), Ct);

        error.Should().BeNull();
        dto.Should().NotBeNull();
    }
}
