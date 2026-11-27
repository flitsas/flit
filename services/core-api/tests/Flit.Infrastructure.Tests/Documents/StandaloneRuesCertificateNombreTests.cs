using Flit.Infrastructure.Documents;
using Flit.Infrastructure.Documents.Standalone;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// Épica #14657 — el certificado RUES standalone (sin trámite ni placa) conserva su nombre anterior
/// <c>certificado_rues_{ref}.pdf</c>; el del trámite (<see cref="RuesCertificatePdfGenerator"/>) usa el estándar
/// <c>{tipo}_{radicado}_{placa}.pdf</c>.
/// </summary>
public sealed class StandaloneRuesCertificateNombreTests
{
    private static readonly IReadOnlyDictionary<string, string?> Campos = new Dictionary<string, string?>
    {
        ["rues_razon_social"] = "EMPRESA DEMO S.A.S.",
        ["rues_nit"] = "900123456",
        ["rues_estado"] = "ACTIVA",
    };

    [Fact]
    public void Standalone_ConservaElNombreAnterior_SinSinPlacaNiSaneoNuevo()
    {
        var renderer = new StandaloneRuesCertificateRenderer(new RuesCertificatePdfGenerator());

        var doc = renderer.Render(Campos, "GD-1a2b3c4d");

        doc.Filename.Should().Be("certificado_rues_GD-1a2b3c4d.pdf");
        doc.Filename.Should().NotContain("SIN-PLACA");
        doc.Mimetype.Should().Be("application/pdf");
        doc.Content.Should().NotBeEmpty();
    }

    [Fact]
    public void Standalone_ElNombreAnteriorReemplazaLaBarraDeLaReferencia()
    {
        StandaloneRuesCertificateRenderer.NombreAnterior("GD/12 34").Should().Be("certificado_rues_GD-12 34.pdf");
    }

    [Fact]
    public void CertificadoDeTramite_SigueConElNombreEstandar()
    {
        var data = new RuesCertificateData(
            Guid.NewGuid(), "FT1-0000102", "EMPRESA DEMO S.A.S.", "900123456", "ACTIVA", Placa: "aaa111");

        new RuesCertificatePdfGenerator().GenerateRuesCertificate(data).Filename
            .Should().Be("certificado_rues_FT1-0000102_AAA111.pdf");
    }
}
