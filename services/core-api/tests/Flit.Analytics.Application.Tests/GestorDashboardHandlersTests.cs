using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries.GestorDashboard;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Analytics.Application.Tests;

/// <summary>
/// Inicio del Gestor (rediseño de los 3 dashboards) — handler de la «Cola de gestión».
/// Uso de ejemplo:
/// <c>await new GestorDashboardColaInactividadHandler(repo).HandleAsync(new(tenant, "preparado", "traspasos", 5), ct)</c>.
/// </summary>
public sealed class GestorDashboardHandlersTests
{
    private readonly IGestorDashboardReadRepository _repo = Substitute.For<IGestorDashboardReadRepository>();
    private static readonly Guid Tenant = Guid.Parse("14a00000-0000-4000-8000-000000000001");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Dos argumentos del mismo tipo: un null literal junto a Arg.Any es ambiguo para NSubstitute.
    private static IReadOnlyList<string>? SinFamilias() => Arg.Is<IReadOnlyList<string>?>(f => f == null);

    // ── Cola de gestión ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cola_SinEstado_DejaFueraLosFinales()
    {
        _repo.GetColaInactividadAsync(Tenant, Arg.Any<IReadOnlyList<string>>(), SinFamilias(), 5, Ct).Returns(new GestorDashboardColaDto([], 0));

        var (_, error) = await new GestorDashboardColaInactividadHandler(_repo)
            .HandleAsync(new GestorDashboardColaInactividadQuery(Tenant, null, null, 5), Ct);

        error.Should().BeNull();
        await _repo.Received(1).GetColaInactividadAsync(
            Tenant,
            Arg.Is<IReadOnlyList<string>>(e => !e.Contains("aprobado") && !e.Contains("anulado") && !e.Contains("revocado")
                && e.Contains("borrador") && e.Contains("subsanacion")),
            SinFamilias(), 5, Ct);
    }

    [Fact]
    public async Task Cola_ConEstadoYFamilia_FiltraPorAmbos()
    {
        var item = new GestorDashboardColaItemDto(Guid.NewGuid(), "TRS-1", "ABC123", null, "preparado", "traspasos", 12);
        _repo.GetColaInactividadAsync(Tenant, Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<string>?>(), 5, Ct)
            .Returns(new GestorDashboardColaDto([item], 7));

        var (dto, error) = await new GestorDashboardColaInactividadHandler(_repo)
            .HandleAsync(new GestorDashboardColaInactividadQuery(Tenant, " Preparado ", "traspasos", 5), Ct);

        error.Should().BeNull();
        dto!.Items.Should().ContainSingle().Which.DiasSinMovimiento.Should().Be(12);
        dto.Total.Should().Be(7);
        await _repo.Received(1).GetColaInactividadAsync(
            Tenant,
            Arg.Is<IReadOnlyList<string>>(e => e.Count == 1 && e[0] == "preparado"),
            Arg.Is<IReadOnlyList<string>?>(f => f!.Count == 1 && f[0] == "TRASPASO"),
            5, Ct);
    }

    [Fact] // Ver por familia: pedir una familia sin Ver no la abre.
    public async Task Cola_FamiliaSinVer_DevuelveVaciaSinConsultar()
    {
        var (dto, error) = await new GestorDashboardColaInactividadHandler(_repo).HandleAsync(
            new GestorDashboardColaInactividadQuery(Tenant, null, "traspasos", 5) { AllowedFamilies = ["MATRICULAS", "OTROS"] }, Ct);

        error.Should().BeNull();
        dto!.Items.Should().BeEmpty();
        dto.Total.Should().Be(0);
        _repo.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("inventado", null, GestorDashboardErrors.InvalidEstado)]
    [InlineData(null, "vehicular", GestorDashboardErrors.InvalidFamilia)]
    public async Task Cola_EntradaInvalida_DevuelveError(string? estado, string? familia, string esperado)
    {
        var (_, error) = await new GestorDashboardColaInactividadHandler(_repo)
            .HandleAsync(new GestorDashboardColaInactividadQuery(Tenant, estado, familia, 5), Ct);

        error.Should().Be(esperado);
        _repo.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(500, 20)]
    public async Task Cola_Limit_SeAcota(int pedido, int efectivo)
    {
        _repo.GetColaInactividadAsync(Tenant, Arg.Any<IReadOnlyList<string>>(), SinFamilias(), efectivo, Ct).Returns(new GestorDashboardColaDto([], 0));

        await new GestorDashboardColaInactividadHandler(_repo)
            .HandleAsync(new GestorDashboardColaInactividadQuery(Tenant, null, null, pedido), Ct);

        await _repo.Received(1).GetColaInactividadAsync(Tenant, Arg.Any<IReadOnlyList<string>>(), SinFamilias(), efectivo, Ct);
    }

    [Fact]
    public async Task Cola_TenantVacio_NoConsulta()
    {
        var (_, error) = await new GestorDashboardColaInactividadHandler(_repo)
            .HandleAsync(new GestorDashboardColaInactividadQuery(Guid.Empty, null, null, 5), Ct);

        error.Should().Be(GestorDashboardErrors.TenantRequired);
        _repo.ReceivedCalls().Should().BeEmpty();
    }
}
