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
    [InlineData("camara_comercio", "persona jurídica o NIT")]
    [InlineData("camara_comercio_comprador", "persona jurídica o NIT")]
    [InlineData("mandato", "mandato o tramitador")]
    [InlineData("contrato_leasing", "leasing")]
    [InlineData("certificado_dijin", "causales de cancelación")]
    [InlineData("oficio_judicial", "causales de cancelación")]
    public void UnDocumentoObligatorioDeUnaCapacidadNoSoportada_ExcluyeAlTipo_NombrandoLaCapacidad(string documento, string capacidad)
    {
        var r = ElegibilidadPersonaNatural.Evaluar(Tipo(documentos: [documento]));

        r.Elegible.Should().BeFalse();
        r.Motivo.Should().Contain(capacidad);
    }

    [Theory]
    [InlineData("""{"requiresLessee":true}""", "leasing")]
    [InlineData("""{"hasPrendaGate":true}""", "acreedor prendario")]
    [InlineData("""{"transitOfficeSource":"OPERATOR"}""", "operador")]
    [InlineData("""{"requiresDestinationTransitOffice":true}""", "organismo de destino")]
    [InlineData("""{"requiresPlateRequest":true}""", "placa")]
    public void UnFlagDelPerfilDeUnaCapacidadNoSoportada_ExcluyeAlTipo(string json, string capacidad)
    {
        var r = ElegibilidadPersonaNatural.Evaluar(Tipo(perfil: Perfil(json)));

        r.Elegible.Should().BeFalse();
        r.Motivo.Should().Contain(capacidad);
    }

    [Fact]
    public void UnTipoPrendarioPorSuCodigo_NoEsElegible_AunqueElPerfilNoDigaNada()
    {
        ElegibilidadPersonaNatural.Evaluar(Tipo("PRENDA_INSCRIPCION")).Elegible.Should().BeFalse();
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
    public void VariasCapacidades_SeReportanTodas()
    {
        var r = ElegibilidadPersonaNatural.Evaluar(Tipo(perfil: Perfil("""{"requiresLessee":true}"""), documentos: ["mandato"]));

        r.Motivos.Should().HaveCount(2);
    }

    [Fact]
    public void TipoNoPublicadoOInactivo_NoEsElegible()
    {
        var t = Tipo();
        ElegibilidadPersonaNatural.Evaluar(t with { Publicado = false }).Elegible.Should().BeFalse();
        ElegibilidadPersonaNatural.Evaluar(t with { Activo = false }).Elegible.Should().BeFalse();
    }
}
