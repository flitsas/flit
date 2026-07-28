using System.Text;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>Tests del "Certificado de vigencia SOAT Y RTM" con membrete FLIT (HU #10856).</summary>
public sealed class SoatRtmCertificatePdfGeneratorTests
{
    private static readonly SoatRtmCertificatePdfGenerator Gen = new();

    private static SoatRtmCertificateData Data(SoatRtmBlock soat, SoatRtmBlock? rtm, AvaluoInfo? avaluo = null) =>
        new(Guid.NewGuid(), "TRM-2026-000777", "ABC123", "2026-07-21", soat, rtm, avaluo);

    [Fact]
    public void Generate_ProducesPdf_WithTipoCertificadoSoatRtm()
    {
        var doc = Gen.GenerateSoatRtmCertificate(Data(
            new SoatRtmBlock(FechaVencimiento: "2027-01-15", Entidad: "La Previsora S.A.", Estado: "VIGENTE"),
            new SoatRtmBlock(FechaVencimiento: "2027-03-20", Estado: "VIGENTE")));

        Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
        doc.Tipo.Should().Be("certificado_soat_rtm");
        doc.Mimetype.Should().Be("application/pdf");
        doc.Filename.Should().Be("certificado_soat_rtm_TRM-2026-000777.pdf");
    }

    [Fact]
    public void Generate_ConAvaluo_Traspaso_ProducePdf()
    {
        var doc = Gen.GenerateSoatRtmCertificate(Data(
            new SoatRtmBlock(FechaVencimiento: "2027-01-15"),
            new SoatRtmBlock(FechaVencimiento: "2027-03-20"),
            new AvaluoInfo([
                new AvaluoRow("AVALÚO FASECOLDA", "$ 45.000.000"),
                new AvaluoRow("AVALÚO COMERCIAL", "$ 42.000.000"),
            ])));

        Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void Generate_SinRtm_Matricula_ProducePdf()
    {
        // Matrícula inicial: Rtm null → se oculta la tabla RTM, sin excepción.
        var doc = Gen.GenerateSoatRtmCertificate(Data(
            new SoatRtmBlock(FechaVencimiento: "2027-01-15"),
            rtm: null!));

        Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void Generate_ConCamposEnBlanco_NoLanza()
    {
        // Regla HU #10856: valores ausentes en la consulta → en blanco, sin excepción.
        var act = () => Gen.GenerateSoatRtmCertificate(Data(new SoatRtmBlock(), new SoatRtmBlock()));

        act.Should().NotThrow();
    }
}
