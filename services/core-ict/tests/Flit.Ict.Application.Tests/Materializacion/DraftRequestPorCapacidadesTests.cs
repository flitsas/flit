using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Infrastructure.ExternalClients;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Materializacion;

/// <summary>
/// ADR-0050 — el borrador que ICT materializa se arma con lo que el mapeo declara del tipo, no con
/// el texto del código ni con el número de transacción.
/// <para>Antes, el organismo de tránsito del RUNT se enviaba solo si el código contenía la palabra
/// «TRASPASO», y los datos comerciales solo si el número era exactamente 3. Con los 16 tipos
/// apuntando a sus codes canónicos, ambas heurísticas fallaban en silencio: un
/// TRASPASO_UNILATERAL perdía el organismo si alguien renombraba el código, y ningún tipo distinto
/// del 3 podía llevar valor de venta.</para>
/// </summary>
public sealed class DraftRequestPorCapacidadesTests
{
    private static ExternalIntegrationMaster Master(int transactionType) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        TransactionType = transactionType,
        Plate = "IWL38D",
        RuntTransitOfficeName = "SECRETARÍA DE MOVILIDAD DE BOGOTÁ",
        SellingPrice = 45_000_000m,
        SellingDate = "2026-08-20",
    };

    private static IAttachmentDocTypeResolver SinAdjuntos()
    {
        var resolver = Substitute.For<IAttachmentDocTypeResolver>();
        resolver.ResolveDocTypeAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("otro"));
        return resolver;
    }

    [Fact]
    public async Task ElOrganismoDelRuntViajaCuandoElTipoLoDeclara()
    {
        var tipo = new DraftProcedureType(
            "TRASPASO_UNILATERAL", "TRASPASO",
            RequiresCommercialValue: false, ResolvesTransitOfficeFromRunt: true);

        var request = await IctGrpcProcedureDraftClient.BuildRequestAsync(
            Master(4), tipo, SinAdjuntos(), log: null, TestContext.Current.CancellationToken);

        request.TransitOfficeName.Should().Be("SECRETARÍA DE MOVILIDAD DE BOGOTÁ");
        request.ProcedureTypeCode.Should().Be("TRASPASO_UNILATERAL");
    }

    [Fact]
    public async Task UnCodigoDeLaFamiliaTraspasoQueNoDiceTraspaso_ConservaElOrganismo()
    {
        // El caso que la heurística por substring perdía en silencio.
        var tipo = new DraftProcedureType(
            "TRANSFERENCIA_DOMINIO", "TRASPASO",
            RequiresCommercialValue: false, ResolvesTransitOfficeFromRunt: true);

        var request = await IctGrpcProcedureDraftClient.BuildRequestAsync(
            Master(4), tipo, SinAdjuntos(), log: null, TestContext.Current.CancellationToken);

        request.TransitOfficeName.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SinLaCapacidad_ElGestorAsignaElOrganismo()
    {
        var tipo = new DraftProcedureType(
            "MATRICULA_LEASING", "MATRICULAS",
            RequiresCommercialValue: false, ResolvesTransitOfficeFromRunt: false);

        var request = await IctGrpcProcedureDraftClient.BuildRequestAsync(
            Master(2), tipo, SinAdjuntos(), log: null, TestContext.Current.CancellationToken);

        request.TransitOfficeName.Should().BeEmpty();
    }

    [Fact]
    public async Task LosDatosComercialesLosPideElTipo_NoElNumeroTres()
    {
        var tipo = new DraftProcedureType(
            "TRASPASO_STANDARD", "TRASPASO",
            RequiresCommercialValue: true, ResolvesTransitOfficeFromRunt: true);

        // Número 4, no 3: la capacidad manda.
        var request = await IctGrpcProcedureDraftClient.BuildRequestAsync(
            Master(4), tipo, SinAdjuntos(), log: null, TestContext.Current.CancellationToken);

        request.Commercial.Should().NotBeNull();
        request.Commercial.SellingDate.Should().Be("2026-08-20");
    }

    [Fact]
    public async Task UnTramiteDeOtrosNoLlevaValorDeVenta()
    {
        var tipo = new DraftProcedureType(
            "BLINDAJE", "OTROS",
            RequiresCommercialValue: false, ResolvesTransitOfficeFromRunt: false);

        var request = await IctGrpcProcedureDraftClient.BuildRequestAsync(
            Master(5), tipo, SinAdjuntos(), log: null, TestContext.Current.CancellationToken);

        request.Commercial.Should().BeNull();
    }
}
