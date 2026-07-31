using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU #11133 (Feature #11131) — snapshot congelado de la consulta al RUES, por NIT y por trámite.
/// Es la fuente del certificado: lo que se consultó al REGISTRAR, no lo que diga el RUES hoy.
/// </summary>
public sealed class RuesSnapshotsTests
{
    private static readonly DateTimeOffset Momento = new(2026, 7, 30, 10, 0, 0, TimeSpan.Zero);

    private static HydratedField[] Campos(string razonSocial) =>
    [
        new("rues_razon_social", razonSocial, null),
        new("rues_estado", "ACTIVA", null),
    ];

    [Fact]
    public void Merge_GuardaLosCamposBajoElNitConsultado()
    {
        var doc = RuesSnapshots.Merge(null, "900511343", Campos("CI TRADE ZONE SAS"), Momento);

        var leido = RuesSnapshots.Read(doc, "900511343");

        leido.Should().NotBeNull();
        leido!["rues_razon_social"].Should().Be("CI TRADE ZONE SAS");
        RuesSnapshots.QueriedAt(doc, "900511343").Should().Be(Momento);
    }

    [Fact]
    public void Read_DeOtroNit_DevuelveNull()
    {
        var doc = RuesSnapshots.Merge(null, "900511343", Campos("CI TRADE ZONE SAS"), Momento);

        RuesSnapshots.Read(doc, "890903938").Should().BeNull();
    }

    [Fact]
    public void Merge_ConservaLasCompaniasAnteriores()
    {
        // El caso que las llaves `rues_*` de instancia no podían cubrir: comprador y vendedor
        // jurídicos en el mismo trámite. Antes solo una de las dos quedaba representada.
        var doc = RuesSnapshots.Merge(null, "900511343", Campos("COMPRADORA SAS"), Momento);
        doc = RuesSnapshots.Merge(doc, "890903938", Campos("VENDEDORA SAS"), Momento);

        RuesSnapshots.Read(doc, "900511343")!["rues_razon_social"].Should().Be("COMPRADORA SAS");
        RuesSnapshots.Read(doc, "890903938")!["rues_razon_social"].Should().Be("VENDEDORA SAS");
    }

    [Fact]
    public void Merge_DelMismoNit_ReemplazaLaEntrada()
    {
        // Reconsultar con el trámite aún en edición es una corrección deliberada del operador.
        var doc = RuesSnapshots.Merge(null, "900511343", Campos("NOMBRE VIEJO"), Momento);
        doc = RuesSnapshots.Merge(doc, "900511343", Campos("NOMBRE CORREGIDO"), Momento.AddHours(1));

        RuesSnapshots.Read(doc, "900511343")!["rues_razon_social"].Should().Be("NOMBRE CORREGIDO");
        RuesSnapshots.QueriedAt(doc, "900511343").Should().Be(Momento.AddHours(1));
    }

    [Theory]
    [InlineData(" 900511343 ")]
    [InlineData("900511343")]
    public void Read_ToleraEspaciosEnElNit(string consultado)
    {
        var doc = RuesSnapshots.Merge(null, " 900511343 ", Campos("CI TRADE ZONE SAS"), Momento);

        RuesSnapshots.Read(doc, consultado).Should().NotBeNull();
    }

    [Fact]
    public void Merge_SinCampos_NoTocaElDocumento()
    {
        var doc = RuesSnapshots.Merge(null, "900511343", Campos("CI TRADE ZONE SAS"), Momento);

        RuesSnapshots.Merge(doc, "890903938", [], Momento).Should().Be(doc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Merge_SinNit_NoTocaElDocumento(string? nit)
    {
        RuesSnapshots.Merge(null, nit, Campos("X"), Momento).Should().BeNull();
    }

    [Fact]
    public void Read_DocumentoCorrupto_NoLanza()
    {
        // Un JSON ilegible no puede tumbar la generación del expediente: se degrada a "sin snapshot".
        RuesSnapshots.Read("{esto no es json", "900511343").Should().BeNull();
        RuesSnapshots.Read("[]", "900511343").Should().BeNull();
    }

    [Fact]
    public void Read_SinDocumento_DevuelveNull()
    {
        RuesSnapshots.Read(null, "900511343").Should().BeNull();
        RuesSnapshots.Read("", "900511343").Should().BeNull();
    }
}
