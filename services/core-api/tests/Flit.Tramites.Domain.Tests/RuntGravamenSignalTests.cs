using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// Bug #13203 — señal única de gravamen. El RUNT puede contestar banderas «NO» y aun así traer una
/// garantía mobiliaria registrada en el RNGM: el detalle (<c>runt_gravamenes</c>) también es señal.
/// Uso de ejemplo:
/// <code>RuntGravamenSignal.Reporta(instance.FieldValues)</code>
/// </summary>
public sealed class RuntGravamenSignalTests
{
    private const string UnaGarantia =
        """[{"idPrenda":"1000001","nombreAcreedor":"BANCO DE PRUEBA S.A.","numeroDocumentoAcreedor":"900000001"}]""";

    private static ProcedureInstanceFieldValue Fv(string key, string? text = null, string? json = null) =>
        new() { FieldKey = key, ValueText = text, ValueJson = json };

    private static List<ProcedureInstanceFieldValue> BanderasNo(params ProcedureInstanceFieldValue[] extra) =>
        [Fv("runt_tiene_prendas", "NO"), Fv("runt_tiene_gravamenes", "NO"), .. extra];

    [Fact]
    public void BanderasNo_ConGarantiaEnElDetalle_ReportaGravamen()
    {
        RuntGravamenSignal.Reporta(BanderasNo(Fv("runt_gravamenes", json: UnaGarantia)))
            .Should().BeTrue("una garantía mobiliaria registrada es gravamen aunque las banderas digan NO");
    }

    [Fact]
    public void BanderasNo_ConDetalleSoloEnValueText_ReportaGravamen()
    {
        RuntGravamenSignal.Reporta(BanderasNo(Fv("runt_gravamenes", text: UnaGarantia)))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{no es json")]
    [InlineData("""{"idPrenda":"1"}""")]
    [InlineData("null")]
    public void BanderasNo_ConDetalleVacioOInvalido_NoInventaGravamen(string detalle)
    {
        RuntGravamenSignal.Reporta(BanderasNo(Fv("runt_gravamenes", json: detalle)))
            .Should().BeFalse();
    }

    [Fact]
    public void BanderasNo_SinDetalle_NoReportaGravamen() =>
        RuntGravamenSignal.Reporta(BanderasNo()).Should().BeFalse();

    [Theory]
    [InlineData("SÍ")]
    [InlineData("si")]
    [InlineData(" S ")]
    [InlineData("true")]
    [InlineData("1")]
    public void BanderaAfirmativa_ReportaGravamen(string bandera)
    {
        RuntGravamenSignal.Reporta(prendas: bandera, gravamenes: "NO", detalleJson: null).Should().BeTrue();
        RuntGravamenSignal.Reporta(prendas: null, gravamenes: bandera, detalleJson: null).Should().BeTrue();
    }

    [Fact]
    public void ContarGarantias_CuentaElementosDelArray()
    {
        RuntGravamenSignal.ContarGarantias("""[{"entidad":"BANCO DE PRUEBA S.A."},{"idPrenda":1000001}]""").Should().Be(2);
        RuntGravamenSignal.ContarGarantias("{roto").Should().Be(0);
        RuntGravamenSignal.ContarGarantias(null).Should().Be(0);
    }

    // Revisión PR #504 (O3) — cuenta lo mismo que el normalizador: solo objetos con nombre, documento,
    // idPrenda o fecha (en cualquiera de sus alias). null, escalares, {} y objetos sin valor no cuentan.
    [Fact]
    public void ContarGarantias_IgnoraNullNoObjetosYObjetosSinValor()
    {
        const string json = """
            [ null, 5, "x", [], {}, { "estado": "Registro" }, { "idPrenda": "", "entidad": "  " },
              { "entidad": "BANCO DE PRUEBA S.A." }, { "IdPrenda": 1000001 }, { "fechaRegistro": "30/09/2026" } ]
            """;

        RuntGravamenSignal.ContarGarantias(json).Should().Be(3);
        RuntGravamenSignal.Reporta(BanderasNo(Fv("runt_gravamenes", json: "[{}]"))).Should().BeFalse();
    }
}
