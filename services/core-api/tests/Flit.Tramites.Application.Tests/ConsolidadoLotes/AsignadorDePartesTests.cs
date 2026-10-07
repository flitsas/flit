using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13377 (AC1, AC2, AC3) — <see cref="AsignadorDePartes"/>: reparto avaro por N y M, PDF mayor que M solo,
/// omitidos en la primera parte que sale, parte solo con CSV y equivalencia «parcial tras cada PDF + final» = «todo
/// de una vez». La persistencia del plan la cubre <c>ConsolidadoLotePartesIntegrationTests</c>.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var plan = AsignadorDePartes.Planear([Pdf(1, 5)], maxPdfs: 3, maxBytes: Mb(10), ModoAsignacion.Final, loteSinPartes: true);
/// </code>
/// </remarks>
public sealed class AsignadorDePartesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 19, 30, 0, TimeSpan.Zero);

    private static long Mb(int mb) => AsignadorDePartes.MbABytes(mb);

    private static ItemSinParte Pdf(int pos, long mb = 1, int segundo = -1) =>
        new(Guid.NewGuid(), true, mb * AsignadorDePartes.BytesPorMb, T0.AddSeconds(segundo < 0 ? pos : segundo), pos);

    private static ItemSinParte Omitido(int pos) => new(Guid.NewGuid(), false, 0, T0.AddSeconds(pos), pos);

    // ── AC1 ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AC1_NMasUnoPdf_DanExactamenteDosPartes_YNingunaSuperaN()
    {
        var pdfs = Enumerable.Range(0, 4).Select(i => Pdf(i)).ToArray();

        var plan = AsignadorDePartes.Planear(pdfs, maxPdfs: 3, maxBytes: Mb(250), ModoAsignacion.Final, loteSinPartes: true);

        plan.Should().HaveCount(2);
        plan.Should().OnlyContain(p => p.Pdfs.Count <= 3);
        plan[0].Pdfs.Should().Equal(pdfs[0].Id, pdfs[1].Id, pdfs[2].Id);
        plan[1].Pdfs.Should().Equal(pdfs[3].Id);
    }

    [Fact]
    public void AC1_Parcial_SoloSacaLaParteLlena_YElRestoEspera()
    {
        var pdfs = Enumerable.Range(0, 4).Select(i => Pdf(i)).ToArray();

        var plan = AsignadorDePartes.Planear(pdfs, 3, Mb(250), ModoAsignacion.Parcial, loteSinPartes: true);

        plan.Should().ContainSingle().Which.Pdfs.Should().HaveCount(3, "el cuarto PDF espera al cierre del carril");
        AsignadorDePartes.Planear(pdfs[..2], 3, Mb(250), ModoAsignacion.Parcial, true)
            .Should().BeEmpty("con 2 de 3 la parte no está llena");
    }

    [Fact]
    public void AC1_RepartoEnTandas_EsIgualAlRepartoDeUnaVez()
    {
        // Tamaños variados: el corte por M y por N se mezcla.
        long[] tamanos = [4, 3, 2, 6, 1, 1, 1, 9, 3, 12, 2, 2, 2, 2];
        var pdfs = tamanos.Select((mb, i) => Pdf(i, mb)).ToArray();

        var deUnaVez = AsignadorDePartes.Planear(pdfs, 4, Mb(10), ModoAsignacion.Final, true)
            .Select(p => p.Pdfs.ToArray()).ToList();

        var enTandas = new List<Guid[]>();
        var pendientes = new List<ItemSinParte>();
        foreach (var pdf in pdfs)
        {
            pendientes.Add(pdf);
            foreach (var parte in AsignadorDePartes.Planear(pendientes, 4, Mb(10), ModoAsignacion.Parcial, enTandas.Count == 0))
            {
                enTandas.Add([.. parte.Pdfs]);
                pendientes.RemoveAll(i => parte.Pdfs.Contains(i.Id));
            }
        }

        enTandas.AddRange(AsignadorDePartes.Planear(pendientes, 4, Mb(10), ModoAsignacion.Final, enTandas.Count == 0)
            .Select(p => p.Pdfs.ToArray()));

        enTandas.Should().BeEquivalentTo(deUnaVez, o => o.WithStrictOrdering());
    }

    [Fact]
    public void AC1_Orden_PorProcesadoEn_YDesempatePorPosicion()
    {
        var tarde = Pdf(0, segundo: 50);
        var temprano = Pdf(5, segundo: 10);
        var empateA = Pdf(2, segundo: 20);
        var empateB = Pdf(1, segundo: 20);

        var plan = AsignadorDePartes.Planear([tarde, temprano, empateA, empateB], 10, Mb(250), ModoAsignacion.Final, true);

        plan.Single().Pdfs.Should().Equal(temprano.Id, empateB.Id, empateA.Id, tarde.Id);
    }

    // ── AC2 ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AC2_PdfMayorQueM_VaSolo_YNingunaOtraParteSuperaM()
    {
        ItemSinParte[] pdfs = [Pdf(0, 4), Pdf(1, 15), Pdf(2, 4), Pdf(3, 4), Pdf(4, 4)];

        var plan = AsignadorDePartes.Planear(pdfs, 500, Mb(10), ModoAsignacion.Final, true);

        plan.Select(p => p.Pdfs.Count).Should().Equal(1, 1, 2, 1);
        plan[1].Pdfs.Should().Equal(pdfs[1].Id);
        plan[1].Bytes.Should().Be(Mb(15));
        plan.Where((_, k) => k != 1).Should().OnlyContain(p => p.Bytes <= Mb(10));
    }

    [Fact]
    public void AC2_ExactamenteM_CierraLaParte_EnModoParcial()
    {
        var plan = AsignadorDePartes.Planear([Pdf(0, 6), Pdf(1, 4)], 500, Mb(10), ModoAsignacion.Parcial, true);

        plan.Should().ContainSingle().Which.Bytes.Should().Be(Mb(10));
    }

    [Fact]
    public void AC2_PdfMayorQueM_EnParcial_SaleEnseguida_YCierraLaEnCurso()
    {
        ItemSinParte[] pdfs = [Pdf(0, 3), Pdf(1, 15)];

        var plan = AsignadorDePartes.Planear(pdfs, 500, Mb(10), ModoAsignacion.Parcial, true);

        plan.Select(p => p.Pdfs.Single()).Should().Equal(pdfs[0].Id, pdfs[1].Id);
    }

    // ── AC3 y omitidos ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AC3_TodoOmitido_UnaUnicaParte_SoloConOmitidos()
    {
        ItemSinParte[] omitidos = [Omitido(2), Omitido(0), Omitido(1)];

        var plan = AsignadorDePartes.Planear(omitidos, 3, Mb(10), ModoAsignacion.Final, loteSinPartes: true);

        var parte = plan.Should().ContainSingle().Subject;
        parte.Pdfs.Should().BeEmpty();
        parte.Bytes.Should().Be(0);
        parte.Omitidos.Should().Equal(omitidos[1].Id, omitidos[2].Id, omitidos[0].Id);
    }

    [Fact]
    public void AC3_LoteSinItems_UnaParteVacia_ParaQueElLoteTermine()
    {
        var plan = AsignadorDePartes.Planear([], 3, Mb(10), ModoAsignacion.Final, loteSinPartes: true);

        var parte = plan.Should().ContainSingle().Subject;
        parte.Pdfs.Should().BeEmpty();
        parte.Omitidos.Should().BeEmpty();
    }

    [Fact]
    public void AC3_Final_SinNadaPendiente_YConPartes_NoCreaNada()
    {
        AsignadorDePartes.Planear([], 3, Mb(10), ModoAsignacion.Final, loteSinPartes: false).Should().BeEmpty();
    }

    [Fact]
    public void AC3_Final_SoloOmitidosTardios_YConPartes_SaleUnaParteSoloConCsv()
    {
        var tardio = Omitido(9);

        var plan = AsignadorDePartes.Planear([tardio], 3, Mb(10), ModoAsignacion.Final, loteSinPartes: false);

        plan.Should().ContainSingle().Which.Should().BeEquivalentTo(new PartePlaneada([], [tardio.Id], 0));
    }

    [Fact]
    public void Omitidos_VanEnLaPrimeraParteQueSale_CadaParteConLosSuyos()
    {
        ItemSinParte[] items = [Omitido(0), Pdf(1), Pdf(2), Omitido(3), Pdf(4), Pdf(5)];

        var plan = AsignadorDePartes.Planear(items, 2, Mb(250), ModoAsignacion.Final, true);

        plan.Should().HaveCount(2);
        plan[0].Omitidos.Should().Equal(items[0].Id, items[3].Id);
        plan[1].Omitidos.Should().BeEmpty();
    }

    [Fact]
    public void Omitidos_EnParcialSinParteLlena_Esperan()
    {
        AsignadorDePartes.Planear([Omitido(0), Pdf(1)], 3, Mb(10), ModoAsignacion.Parcial, true).Should().BeEmpty();
    }

    // ── Contrato ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 10)]
    [InlineData(3, 0)]
    public void Contrato_NoMInvalidos_Lanzan(int maxPdfs, long maxBytes)
    {
        var act = () => AsignadorDePartes.Planear([Pdf(0)], maxPdfs, maxBytes, ModoAsignacion.Final, true);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Contrato_MbABytes_UsaMiB()
    {
        AsignadorDePartes.MbABytes(10).Should().Be(10L * 1024 * 1024);
    }

    [Fact]
    public void Contrato_CadaItemSaleUnaSolaVez()
    {
        var items = Enumerable.Range(0, 37).Select(i => i % 5 == 0 ? Omitido(i) : Pdf(i, (i % 7) + 1)).ToArray();

        var plan = AsignadorDePartes.Planear(items, 4, Mb(10), ModoAsignacion.Final, true);

        plan.SelectMany(p => p.Pdfs.Concat(p.Omitidos)).Should().BeEquivalentTo(items.Select(i => i.Id))
            .And.OnlyHaveUniqueItems();
        plan.Should().OnlyContain(p => p.Pdfs.Count <= 4 && (p.Bytes <= Mb(10) || p.Pdfs.Count == 1));
    }
}
