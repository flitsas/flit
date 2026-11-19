using Flit.Analytics.Application.Queries.SuperAdminDashboard;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Analytics.Application.Tests.SuperAdminDashboard;

/// <summary>Inicio del SuperAdmin — validación de los handlers de compañías y organismos.</summary>
public sealed class SuperAdminDashboardHandlersTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateOnly Desde = new(2026, 11, 1);
    private static readonly DateOnly Hasta = new(2026, 11, 30);
    private readonly ISuperAdminDashboardReadRepository _repo = Substitute.For<ISuperAdminDashboardReadRepository>();

    [Fact]
    public async Task Companias_sin_rango_usa_7_dias_y_pasa_las_familias()
    {
        IReadOnlyList<string> familias = ["MATRICULAS"];
        _repo.GetCompaniesAsync(null, null, 7, familias, Ct).Returns([]);

        var (result, error) = await new SuperAdminDashboardCompaniesHandler(_repo)
            .HandleAsync(new SuperAdminDashboardCompaniesQuery(null, null) { AllowedFamilies = familias }, Ct);

        error.Should().BeNull();
        result!.StuckDays.Should().Be(7);
        result.From.Should().BeNull();
        await _repo.Received(1).GetCompaniesAsync(null, null, 7, familias, Ct);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public async Task Companias_con_dias_fuera_de_1_a_90_falla_sin_consultar(int dias)
    {
        var (_, error) = await new SuperAdminDashboardCompaniesHandler(_repo)
            .HandleAsync(new SuperAdminDashboardCompaniesQuery(null, null, dias), Ct);

        error.Should().Be("invalid_stuck_days");
        _repo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Rango_invertido_falla_en_los_dos_handlers()
    {
        (await new SuperAdminDashboardCompaniesHandler(_repo).HandleAsync(new SuperAdminDashboardCompaniesQuery(Hasta, Desde), Ct))
            .Error.Should().Be("invalid_range");
        (await new SuperAdminDashboardOfficesHandler(_repo).HandleAsync(new SuperAdminDashboardOfficesQuery(null, Hasta, Desde), Ct))
            .Error.Should().Be("invalid_range");
        _repo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Organismos_un_solo_extremo_es_valido()
    {
        var vacio = new SuperAdminDashboardOfficesDto(null, Desde, null, 0, null, []);
        _repo.GetOfficesAsync(null, Desde, null, null, Ct).Returns(vacio);

        var (result, error) = await new SuperAdminDashboardOfficesHandler(_repo)
            .HandleAsync(new SuperAdminDashboardOfficesQuery(null, Desde, null), Ct);

        error.Should().BeNull();
        result.Should().BeSameAs(vacio);
    }

    [Fact]
    public async Task Organismos_con_Guid_Empty_se_lee_como_todas_las_companias()
    {
        _repo.GetOfficesAsync(null, null, null, null, Ct).Returns(new SuperAdminDashboardOfficesDto(null, null, null, 0, null, []));

        await new SuperAdminDashboardOfficesHandler(_repo).HandleAsync(new SuperAdminDashboardOfficesQuery(Guid.Empty, null, null), Ct);

        await _repo.Received(1).GetOfficesAsync(null, null, null, null, Ct);
    }
}
