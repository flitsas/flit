using Flit.Tramites.Application.UseCases.RevocationRequests;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.RevocationRequests;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.RevocationRequests;

/// <summary>
/// HU #12578 (Feature #12565) — «Listar solicitudes de revocatoria» del lado gestor: alimenta la vista
/// dedicada "Revocatorias" con los mismos filtros (fecha, OT, estado) que pide el AC1. Repositorio real
/// (<c>ProcedureRevocationRequestRepository</c>, con el JOIN/WHERE SQL) queda fuera de este proyecto
/// (no referencia <c>Flit.Infrastructure</c>) — aquí se prueba el WIRING del handler: normalización de
/// paginación, mapeo a DTO y que el filtro/tenant que arma llegue intacto al repositorio.
///
/// <para>
/// Uso de ejemplo:
/// <code>
/// var result = await new ListRevocationRequestsHandler(repo)
///     .HandleAsync(new ListRevocationRequestsQuery(tenantId, null, null, null, null, null, null));
/// </code>
/// </para>
/// </summary>
public sealed class ListRevocationRequestsHandlerTests
{
    private readonly IProcedureRevocationRequestRepository _repo = Substitute.For<IProcedureRevocationRequestRepository>();
    private static readonly Guid TenantId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private ListRevocationRequestsHandler Handler() => new(_repo);

    private static RevocationRequestListItem Item(string status = ProcedureRevocationRequestStatus.Solicitada) => new(
        RevocationRequestId: Guid.NewGuid(),
        ProcedureInstanceId: Guid.NewGuid(),
        ReferenceNumber: "RAD-2026-001",
        Placa: "ABC123",
        TransitOfficeId: Guid.NewGuid(),
        TransitOfficeName: "OT Bogotá",
        Status: status,
        AttemptNumber: 1,
        RequestedAt: DateTimeOffset.UtcNow.AddDays(-1),
        DecidedAt: null);

    // ── AC1 — filtros (fecha, OT, estado) llegan intactos al repositorio ──────────────────────────

    [Fact]
    public async Task HandleAsync_ConFiltrosCompletos_LosPasaIntactosAlRepositorioConElTenantCorrecto()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-30);
        var to = DateTimeOffset.UtcNow;
        var officeId = Guid.NewGuid();
        _repo.ListForTenantAsync(TenantId, Arg.Any<RevocationRequestListFilter>(), Arg.Any<CancellationToken>())
            .Returns(RevocationRequestListPage.Empty);

        await Handler().HandleAsync(new ListRevocationRequestsQuery(
            TenantId,
            Statuses: [ProcedureRevocationRequestStatus.Rechazada],
            RequestedFrom: from,
            RequestedTo: to,
            TransitOfficeId: officeId,
            Skip: 10,
            Take: 5),
            TestContext.Current.CancellationToken);

        await _repo.Received(1).ListForTenantAsync(
            TenantId,
            Arg.Is<RevocationRequestListFilter>(f =>
                f.Statuses!.Single() == ProcedureRevocationRequestStatus.Rechazada
                && f.RequestedFrom == from
                && f.RequestedTo == to
                && f.TransitOfficeId == officeId
                && f.Skip == 10
                && f.Take == 5),
            Arg.Any<CancellationToken>());
    }

    // ── Paginación — defaults y tope (compartidos con el lado OT) ─────────────────────────────────

    [Theory]
    [InlineData(null, 20)]
    [InlineData(0, 20)]
    [InlineData(-5, 20)]
    [InlineData(500, 100)]
    [InlineData(7, 7)]
    public async Task HandleAsync_NormalizaTake_DefaultYTope(int? take, int esperado)
    {
        _repo.ListForTenantAsync(TenantId, Arg.Any<RevocationRequestListFilter>(), Arg.Any<CancellationToken>())
            .Returns(RevocationRequestListPage.Empty);

        var result = await Handler().HandleAsync(
            new ListRevocationRequestsQuery(TenantId, null, null, null, null, null, take),
            TestContext.Current.CancellationToken);

        result.Take.Should().Be(esperado);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(-1, 0)]
    [InlineData(15, 15)]
    public async Task HandleAsync_NormalizaSkip(int? skip, int esperado)
    {
        _repo.ListForTenantAsync(TenantId, Arg.Any<RevocationRequestListFilter>(), Arg.Any<CancellationToken>())
            .Returns(RevocationRequestListPage.Empty);

        var result = await Handler().HandleAsync(
            new ListRevocationRequestsQuery(TenantId, null, null, null, null, skip, null),
            TestContext.Current.CancellationToken);

        result.Skip.Should().Be(esperado);
    }

    // ── Contrato — mapeo a DTO conserva todos los campos de la fila ───────────────────────────────

    [Fact]
    public async Task HandleAsync_MapeaCadaItemAlDtoConTodosLosCampos()
    {
        var item = Item(ProcedureRevocationRequestStatus.EnRevision);
        _repo.ListForTenantAsync(TenantId, Arg.Any<RevocationRequestListFilter>(), Arg.Any<CancellationToken>())
            .Returns(new RevocationRequestListPage([item], TotalCount: 1));

        var result = await Handler().HandleAsync(
            new ListRevocationRequestsQuery(TenantId, null, null, null, null, null, null),
            TestContext.Current.CancellationToken);

        result.Total.Should().Be(1);
        var dto = result.Items.Single();
        dto.RevocationRequestId.Should().Be(item.RevocationRequestId);
        dto.ProcedureInstanceId.Should().Be(item.ProcedureInstanceId);
        dto.ReferenceNumber.Should().Be(item.ReferenceNumber);
        dto.Placa.Should().Be(item.Placa);
        dto.TransitOfficeId.Should().Be(item.TransitOfficeId);
        dto.TransitOfficeName.Should().Be(item.TransitOfficeName);
        dto.Status.Should().Be(ProcedureRevocationRequestStatus.EnRevision);
        dto.AttemptNumber.Should().Be(item.AttemptNumber);
        dto.RequestedAt.Should().Be(item.RequestedAt);
        dto.DecidedAt.Should().Be(item.DecidedAt);
    }

    [Fact]
    public async Task HandleAsync_SinResultados_DevuelvePaginaVaciaConTotalCero()
    {
        _repo.ListForTenantAsync(TenantId, Arg.Any<RevocationRequestListFilter>(), Arg.Any<CancellationToken>())
            .Returns(RevocationRequestListPage.Empty);

        var result = await Handler().HandleAsync(
            new ListRevocationRequestsQuery(TenantId, null, null, null, null, null, null),
            TestContext.Current.CancellationToken);

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
    }
}
