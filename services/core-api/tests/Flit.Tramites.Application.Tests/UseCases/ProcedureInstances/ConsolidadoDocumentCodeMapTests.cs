using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #11183 — correspondencia código del catálogo ↔ tipo del adjunto. Sin ella, reordenar el
/// catálogo no tendría efecto sobre los documentos cuyo adjunto se guarda con otro nombre.
/// </summary>
public sealed class ConsolidadoDocumentCodeMapTests
{
    [Fact]
    public void AC1_CodigoConAdjuntoDeOtroNombre_SeAsociaExplicitamente()
    {
        // El certificado de vigencia SOAT/RTM se cataloga con un código y se adjunta con otro.
        ConsolidadoDocumentCodeMap.AttachmentTipos("certificado_vigencia_soat_rtm")
            .Should().ContainSingle().Which.Should().Be("certificado_soat_rtm");
    }

    [Fact]
    public void AC1_CodigoSinEquivalencia_SeMapeaASiMismo()
    {
        // El catálogo se dio de alta con los mismos códigos que usan los adjuntos (HU #11181).
        ConsolidadoDocumentCodeMap.AttachmentTipos("fur").Should().ContainSingle().Which.Should().Be("fur");
        ConsolidadoDocumentCodeMap.AttachmentTipos("compraventa").Should().ContainSingle().Which.Should().Be("compraventa");
        ConsolidadoDocumentCodeMap.AttachmentTipos("certificado_identidad_vendedor")
            .Should().ContainSingle().Which.Should().Be("certificado_identidad_vendedor");
    }

    [Fact]
    public void ToPrecedence_TraduceElOrdenConfigurado_ConservandoLaPosicion()
    {
        var precedencia = ConsolidadoDocumentCodeMap.ToPrecedence(
            ["soat", "certificado_vigencia_soat_rtm", "fur"]);

        precedencia.Should().ContainInOrder("soat", "certificado_soat_rtm", "fur");
    }

    [Fact]
    public void ToPrecedence_NoDuplicaUnAdjuntoAlcanzadoPorDosCodigos()
    {
        var precedencia = ConsolidadoDocumentCodeMap.ToPrecedence(["fur", "FUR", "fur"]);

        precedencia.Should().ContainSingle().Which.Should().Be("fur");
    }

    [Fact]
    public void ToPrecedence_SinCodigos_DevuelveListaVacia()
    {
        ConsolidadoDocumentCodeMap.ToPrecedence(null).Should().BeEmpty();
        ConsolidadoDocumentCodeMap.ToPrecedence([]).Should().BeEmpty();
    }

    [Fact]
    public void AttachmentTipos_CodigoVacio_DevuelveListaVacia()
    {
        ConsolidadoDocumentCodeMap.AttachmentTipos(null).Should().BeEmpty();
        ConsolidadoDocumentCodeMap.AttachmentTipos("   ").Should().BeEmpty();
    }
}
