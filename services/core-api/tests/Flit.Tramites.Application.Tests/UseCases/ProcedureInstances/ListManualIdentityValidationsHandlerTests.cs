using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>HU #13296 — validación de parámetros, paginación y tiempo en espera del listado manual.</summary>
public sealed class ListManualIdentityValidationsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);

    private readonly IManualIdentityReviewReadRepository _repo = Substitute.For<IManualIdentityReviewReadRepository>();
    private readonly ListManualIdentityValidationsHandler _handler;

    public ListManualIdentityValidationsHandlerTests() =>
        _handler = new ListManualIdentityValidationsHandler(_repo, new RelojFijo(Now));

    private static ManualIdentityReviewRow Row(string status, DateTimeOffset? waitingSince) =>
        new(Guid.NewGuid(), "Nombre", "123", "Compañía", "tramite", status, Now.AddDays(-3), waitingSince);

    [Fact]
    public async Task Solo_la_pendiente_de_revision_calcula_minutos_y_el_resto_devuelve_null()
    {
        _repo.ListAsync(Arg.Any<ManualIdentityReviewFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((
                (IReadOnlyList<ManualIdentityReviewRow>)
                [
                    Row(BiometricEstados.PendienteRevisionManual, Now.AddMinutes(-125).AddSeconds(-30)),
                    Row(BiometricEstados.ManualActivo, Now.AddMinutes(-10)), // regresión: esperar la captura NO es espera de revisión
                    Row(BiometricEstados.Aprobado, Now.AddDays(-2)),
                    Row(BiometricEstados.Rechazado, null),
                    Row(BiometricEstados.PendienteRevisionManual, Now.AddMinutes(5)), // reloj adelantado: nunca negativo
                ],
                5));

        var (result, error) = await _handler.HandleAsync(new ListManualIdentityValidationsQuery(), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.Items.Select(i => i.WaitingMinutes).Should().Equal(125, null, null, null, 0);
        result.Total.Should().Be(5);
    }

    [Theory]
    [InlineData(1, 20, 0, 20)]
    [InlineData(3, 25, 50, 25)]
    [InlineData(0, 5, 0, 10)]       // página < 1 → 1; pageSize mínimo 10
    [InlineData(2, 5000, 100, 100)] // pageSize tope 100
    public async Task Paginacion_se_acota_y_calcula_el_salto(int page, int pageSize, int skip, int take)
    {
        _repo.ListAsync(Arg.Any<ManualIdentityReviewFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ManualIdentityReviewRow>)[], 0));

        var (result, _) = await _handler.HandleAsync(
            new ListManualIdentityValidationsQuery(Page: page, PageSize: pageSize), TestContext.Current.CancellationToken);

        await _repo.Received(1).ListAsync(Arg.Any<ManualIdentityReviewFilter>(), skip, take, Arg.Any<CancellationToken>());
        result!.PageSize.Should().Be(take);
        result.Page.Should().Be(Math.Max(page, 1));
    }

    [Theory]
    [InlineData("kyverum")]
    [InlineData("enviado")]
    public async Task Estado_fuera_de_los_manuales_es_error_y_no_consulta(string status)
    {
        var (result, error) = await _handler.HandleAsync(
            new ListManualIdentityValidationsQuery(Status: status), TestContext.Current.CancellationToken);

        result.Should().BeNull();
        error.Should().Contain("estado");
        _repo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Origen_invalido_es_error_y_no_consulta()
    {
        var (result, error) = await _handler.HandleAsync(
            new ListManualIdentityValidationsQuery(Origin: "otro"), TestContext.Current.CancellationToken);

        result.Should().BeNull();
        error.Should().Contain("origen");
        _repo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void ToFilter_normaliza_minusculas_recorta_y_acota_el_texto()
    {
        var filter = new ListManualIdentityValidationsQuery(
            Status: " Pendiente_Revision_Manual ", Origin: "MANDATARIO", Text: "  " + new string('x', 150) + "  ").ToFilter();

        filter.Status.Should().Be("pendiente_revision_manual");
        filter.Origin.Should().Be("mandatario");
        filter.Text.Should().HaveLength(ListManualIdentityValidationsQuery.TextMaxLength);
        new ListManualIdentityValidationsQuery(Text: "   ").ToFilter().Text.Should().BeNull();
    }

    private sealed class RelojFijo(DateTimeOffset utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utc;
    }
}
