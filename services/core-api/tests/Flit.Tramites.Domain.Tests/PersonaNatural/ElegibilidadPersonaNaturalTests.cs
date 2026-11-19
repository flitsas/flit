using Flit.Tramites.Domain.Tramites.PersonaNatural;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.PersonaNatural;

/// <summary>HU #14079 (Épica #14021) — la elegibilidad sale del perfil del tipo, no de una lista de códigos.</summary>
public sealed class ElegibilidadPersonaNaturalTests
{
    private static ProcedureTypeGateProfile Perfil(string json = "{}") => ProcedureTypeGateProfile.FromJson(json);

    private static PerfilTipoParaElegibilidad Tipo(
        string codigo = "TIPO_X", string familia = "OTROS", bool wizard = true, ProcedureTypeGateProfile? perfil = null, params string[] documentos) =>
        new(codigo, familia, Publicado: true, Activo: true, wizard, perfil ?? Perfil(), documentos);

    [Fact]
    public void SinWizardEncendido_NoEsElegible_YElMotivoDiceQueNoEstaHabilitadoEnFlit2()
    {
        var r = ElegibilidadPersonaNatural.Evaluar(Tipo(wizard: false));

        r.Elegible.Should().BeFalse();
        r.Motivo.Should().Be("El tipo de trámite no está habilitado en FLIT 2.");
    }

    [Fact]
    public void TraspasoStandard_ConElPerfilDelSeed_EsElegibleConMotivoVacio()
    {
        var perfil = Perfil("""{"entryMode":"PLATE","requiresSeller":true,"requiresBuyer":true,"allowsMultipleBuyer":true,"requiresCommercialValue":true,"requiresBiometrics":true,"biometricActors":["OWNER","BUYER"],"requiresSignature":true,"validateOtOperability":true}""");

        var r = ElegibilidadPersonaNatural.Evaluar(Tipo("TRASPASO_STANDARD", "TRASPASO", perfil: perfil, documentos: ["compraventa", "soat", "rtm"]));

        r.Elegible.Should().BeTrue();
        r.Motivo.Should().BeEmpty();
    }

    [Theory]
    [InlineData("camara_comercio")]
    [InlineData("camara_comercio_comprador")]
    [InlineData("mandato")]
    [InlineData("contrato_leasing")]
    [InlineData("certificado_dijin")]
    [InlineData("oficio_judicial")]
    public void UnDocumentoObligatorioDeJuridicaMandatoLeasingOCausal_YaNoExcluyeAlTipo(string documento)
    {
        var r = ElegibilidadPersonaNatural.Evaluar(Tipo(documentos: [documento]));

        r.Elegible.Should().BeTrue();
        r.Motivo.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"requiresLessee":true}""")]
    [InlineData("""{"hasPrendaGate":true}""")]
    [InlineData("""{"transitOfficeSource":"OPERATOR"}""")]
    [InlineData("""{"requiresDestinationTransitOffice":true}""")]
    [InlineData("""{"requiresPlateRequest":true}""")]
    public void UnFlagDelPerfilQueAntesExcluia_YaNoExcluyeAlTipo(string json)
    {
        var r = ElegibilidadPersonaNatural.Evaluar(Tipo(perfil: Perfil(json)));

        r.Elegible.Should().BeTrue();
        r.Motivo.Should().BeEmpty();
    }

    [Fact]
    public void UnTipoPrendarioPorSuCodigo_EsElegible()
    {
        ElegibilidadPersonaNatural.Evaluar(Tipo("PRENDA_INSCRIPCION")).Elegible.Should().BeTrue();
    }

    [Fact]
    public void LaListaDeCapacidadesNoSoportadasEstaVacia()
    {
        ElegibilidadPersonaNatural.Capacidades.Should().BeEmpty();
    }
    [Fact]
    public void DocumentoObligatorioImpronta_NoExcluyeAlTipo()
    {
        var r = ElegibilidadPersonaNatural.Evaluar(Tipo(documentos: ["impronta"]));

        r.Elegible.Should().BeTrue();
        r.Motivo.Should().BeEmpty();
    }

    [Fact]
    public void UnTipoNuevoDeUnSoloTitular_SinCapacidadesExcluyentes_EsElegibleSinTocarCodigo()
    {
        var r = ElegibilidadPersonaNatural.Evaluar(Tipo("TIPO_PUBLICADO_MANANA", "OTROS", documentos: ["tarjeta_propiedad", "doc_identidad"]));

        r.Elegible.Should().BeTrue();
    }


    [Fact]
    public void TipoNoPublicadoOInactivo_NoEsElegible()
    {
        var t = Tipo();
        ElegibilidadPersonaNatural.Evaluar(t with { Publicado = false }).Elegible.Should().BeFalse();
        ElegibilidadPersonaNatural.Evaluar(t with { Activo = false }).Elegible.Should().BeFalse();
    }
}
