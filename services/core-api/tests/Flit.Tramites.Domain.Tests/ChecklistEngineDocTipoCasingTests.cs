using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// El <c>code</c> de un tipo de documento creado desde el módulo Documental conserva las mayúsculas
/// que escribió el administrador (el saneador solo filtra a <c>[A-Za-z0-9-]</c>), pero la subida
/// PERSISTE el tipo del adjunto en minúsculas. Con la comparación sensible a mayúsculas, el
/// documento se subía —fichero en S3, fila en la base— y el checklist seguía diciendo que faltaba:
/// para el gestor, «no carga».
///
/// <para>La comparación no puede resolverse normalizando el catálogo: conviven a propósito códigos
/// con distinto casing (<c>SOAT</c> del seed de organismos y <c>soat</c> del catálogo operativo), así
/// que pasarlos todos a minúsculas los colisionaría contra <c>uq_document_types_code</c>.</para>
/// </summary>
public sealed class ChecklistEngineDocTipoCasingTests
{
    private static ChecklistResultado Compute(
        IReadOnlyList<ChecklistItem> matriz, IReadOnlyCollection<string> docTipos) =>
        ChecklistEngine.ComputeFromMatrix(
            "BLINDAJE", matriz, null, docTipos, new TramiteDocumentContext(), null, null);

    [Theory]
    [InlineData("Certificado-Blindaje", "certificado-blindaje")]
    [InlineData("CertificadoBlindaje", "certificadoblindaje")]
    [InlineData("SOAT", "soat")]
    public void DocumentoSubido_SatisfaceElItem_AunqueElCodigoTengaMayusculas(
        string codigoDelCatalogo, string tipoPersistido)
    {
        IReadOnlyList<ChecklistItem> matriz =
        [
            new ChecklistItem(codigoDelCatalogo, "Certificado de blindaje", Obligatorio: true, DocTipo: codigoDelCatalogo),
        ];

        var r = Compute(matriz, [tipoPersistido]);

        r.Items.Single().Satisfecho.Should().BeTrue();
        r.Items.Single().Via.Should().Be(ChecklistVia.Documento);
        r.FaltanObligatorios.Should().BeEmpty();
        r.Completo.Should().BeTrue();
    }

    [Fact]
    public void SinElDocumento_ElObligatorioSigueFaltando()
    {
        // La contraparte: volver insensible la comparación no puede dar por satisfecho un ítem al que
        // nadie le subió nada.
        IReadOnlyList<ChecklistItem> matriz =
        [
            new ChecklistItem("Certificado-Blindaje", "Certificado de blindaje", Obligatorio: true, DocTipo: "Certificado-Blindaje"),
        ];

        var r = Compute(matriz, ["soat"]);

        r.Items.Single().Satisfecho.Should().BeFalse();
        r.FaltanObligatorios.Should().Equal("Certificado-Blindaje");
        r.Completo.Should().BeFalse();
    }
}
