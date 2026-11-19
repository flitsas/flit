using Flit.Tramites.Domain.Tramites.PersonaNatural;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.PersonaNatural;

/// <summary>HU 1 — el portal es solo para personas naturales: los documentos de jurídica y mandato no se piden.</summary>
public sealed class DocumentosNoAplicanAPersonaNaturalTests
{
    [Theory]
    [InlineData("camara_comercio")]
    [InlineData("camara_comercio_vendedor")]
    [InlineData("camara_comercio_comprador")]
    [InlineData("camara_comercio_locatario")]
    [InlineData("escritura_representante")]
    [InlineData("escritura_representante_vendedor")]
    [InlineData("escritura_representante_locatario")]
    [InlineData("mandato")]
    [InlineData("poder_tramitador")]
    [InlineData("autorizacion_apoderado")]
    [InlineData("MANDATO")]
    public void LosDocumentosDeJuridicaYMandato_NoAplican(string doc) =>
        DefinicionPersonaNatural.DocumentoNoAplicaAPersonaNatural(doc).Should().BeTrue();

    [Theory]
    [InlineData("tarjeta_propiedad")]
    [InlineData("compraventa")]
    [InlineData("contrato_leasing")]
    [InlineData("declaracion_arrendadora")]
    [InlineData("soporte_cambio_color")]
    [InlineData("oficio_judicial")]
    [InlineData("escritura_publica")]
    [InlineData("")]
    [InlineData(null)]
    public void LosDemasDocumentos_Aplican(string? doc) =>
        DefinicionPersonaNatural.DocumentoNoAplicaAPersonaNatural(doc).Should().BeFalse();
}
