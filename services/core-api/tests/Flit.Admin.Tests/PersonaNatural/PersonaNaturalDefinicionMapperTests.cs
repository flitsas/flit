using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Api.Grpc;
using Flit.Infrastructure.PersonaNatural;
using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Flit.Tramites.Domain.Tramites.PersonaNatural;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.PersonaNatural;

/// <summary>
/// HU 1, 4, 10 y 12 — la definición que recibe el portal (puro: sin base ni red). El mapper proyecta lo que FLIT 2 ya sabe del tipo:
/// declaraciones, flags de destino y de organismo, documentos sin los de jurídica/mandato y actores sin la parte que no se captura.
/// </summary>
public sealed class PersonaNaturalDefinicionMapperTests
{
    private static TipoPersonaNatural Tipo(string codigo, string familia, string perfil) => new(
        Guid.NewGuid(), codigo, "Tipo " + codigo, familia, 1, ProcedureTypeGateProfile.FromJson(perfil), ResultadoElegibilidad.Si);

    private static ResolvedDocumentMatrixItem Doc(string codigo, bool obligatorio = true, short orden = 1) =>
        new() { DocumentTypeId = Guid.NewGuid(), Codigo = codigo, Nombre = "Doc " + codigo, Obligatorio = obligatorio, OrdenResuelto = orden };

    private static List<ProcedureStepDto> Pasos() =>
    [
        new(Guid.NewGuid(), "consulta", "Consulta", 1, true, [new(Guid.NewGuid(), "c1", "Consulta", 1, "single", [], "vehicle_query")]),
        new(Guid.NewGuid(), "documentos", "Documentos", 2, true, [new(Guid.NewGuid(), "d1", "Docs", 1, "single", [], "document_checklist")]),
    ];

    private const string PerfilOtros = """{"entryMode":"PLATE","requiresBuyer":true,"allowsComplementaryPrenda":false,"allowsComplementaryTransformations":false}""";

    [Fact]
    public void UnCambioDeColor_LlevaSuDeclaracion_ConLosNombresDelContrato()
    {
        var d = PersonaNaturalDefinicionMapper.Armar(Tipo("CAMBIO_COLOR", "OTROS", PerfilOtros), Pasos(), [Doc("tarjeta_propiedad")]);

        var decl = d.Declaraciones.Should().ContainSingle().Which;
        decl.Clave.Should().Be("transformacion_color");
        decl.Control.Should().Be("seleccion_catalogo");
        decl.Obligatoria.Should().BeTrue();
        decl.FieldKey.Should().Be("vehicle_color");
        decl.FieldKeysExtra.Should().Equal("cambio_color");
        decl.DocCodeSoporte.Should().Be("soporte_cambio_color");
        decl.SoporteObligatorio.Should().BeFalse();
        decl.Catalogo.Should().Be("colores");
        d.Flags.RequiereDestino.Should().BeFalse();
        d.Flags.OrganismoLoEligeOperador.Should().BeFalse();
    }

    [Fact]
    public void Blindaje_LlevaSusOpcionesConSusDocumentos()
    {
        var d = PersonaNaturalDefinicionMapper.Armar(Tipo("BLINDAJE", "OTROS", PerfilOtros), Pasos(), []);

        var decl = d.Declaraciones.Single();
        decl.Opciones.Select(o => o.Codigo).Should().Equal("NIVEL_1", "NIVEL_2", "NIVEL_3", "DESMONTE");
        decl.Opciones.Should().OnlyContain(o => o.ExigeSoporte && o.DocCodes.Count == 1 && o.DocCodes[0] == "certificado_blindaje");
        decl.SoporteObligatorio.Should().BeTrue();
    }

    [Fact]
    public void LaCancelacion_LlevaLasCausalesConSusDocumentos()
    {
        var d = PersonaNaturalDefinicionMapper.Armar(Tipo("CANCELACION_MATRICULA", "MATRICULAS", PerfilOtros), Pasos(), []);

        var decl = d.Declaraciones.Single(x => x.Clave == "cancelacion_causal");
        decl.Opciones.Select(o => o.Codigo).Should().Equal("DECISION_JUDICIAL", "PERDIDA_TOTAL_FUERZA_MAYOR", "PERDIDA_TOTAL_ACCIDENTE", "DECISION_VOLUNTARIA");
        decl.Opciones.Single(o => o.Codigo == "DECISION_JUDICIAL").DocCodes.Should().Equal("oficio_judicial");
    }

    [Fact]
    public void ElTrasladoDeCuenta_PrendeRequiereDestino_YDeclaraElOrganismo()
    {
        var d = PersonaNaturalDefinicionMapper.Armar(
            Tipo("TRASLADO_CUENTA", "OTROS", """{"entryMode":"PLATE","requiresBuyer":true,"requiresDestinationTransitOffice":true,"allowsComplementaryTransformations":false}"""), Pasos(), []);

        d.Flags.RequiereDestino.Should().BeTrue();
        d.Declaraciones.Should().ContainSingle(x => x.Control == "organismo_destino" && x.FieldKey == "transit_office_destino_id");
    }

    [Fact]
    public void UnTipoPorVinOSinRunt_MarcaQueElOrganismoLoEligeElOperador()
    {
        PersonaNaturalDefinicionMapper.Armar(Tipo("MATRICULA_NUEVA", "MATRICULAS", """{"entryMode":"VIN","requiresBuyer":true}"""), Pasos(), []).Flags.OrganismoLoEligeOperador.Should().BeTrue();
        PersonaNaturalDefinicionMapper.Armar(Tipo("RADICADO_CUENTA", "OTROS", """{"entryMode":"PLATE","requiresBuyer":true,"transitOfficeSource":"OPERATOR"}"""), Pasos(), []).Flags.OrganismoLoEligeOperador.Should().BeTrue();
        PersonaNaturalDefinicionMapper.Armar(Tipo("DUPLICADO_TARJETA", "OTROS", PerfilOtros), Pasos(), []).Flags.OrganismoLoEligeOperador.Should().BeFalse();
    }

    [Fact]
    public void LosDocumentosDeJuridicaYMandato_NoSalen_NiDeLaMatrizNiDeLasReglasCondicionales()
    {
        var d = PersonaNaturalDefinicionMapper.Armar(
            Tipo("TRASPASO_STANDARD", "TRASPASO", """{"entryMode":"PLATE","requiresBuyer":true,"requiresSeller":true}"""),
            Pasos(),
            [Doc("compraventa", orden: 1), Doc("camara_comercio_vendedor", orden: 2), Doc("mandato", obligatorio: false, orden: 3), Doc("escritura_representante", orden: 4), Doc("soat", orden: 5)]);

        var codigos = d.Documentos.Select(x => x.DocCode).ToList();
        codigos.Should().Contain(["compraventa", "soat"]);
        codigos.Should().NotContain(["camara_comercio_vendedor", "mandato", "escritura_representante", "poder_tramitador"],
            "el portal es solo de personas naturales; 'mandato' y 'poder_tramitador' también llegan como adicionales de las reglas de traspaso");
        codigos.Should().Contain("contrato_leasing", "el leasing sí aplica: lo pide el locatario");
    }

    [Fact]
    public void LosTramitesSimultaneos_SeDeclaranEnMatriculaYTraspaso_ConLaTransformacionQueLosActiva()
    {
        var d = PersonaNaturalDefinicionMapper.Armar(
            Tipo("TRASPASO_STANDARD", "TRASPASO", """{"entryMode":"PLATE","requiresBuyer":true,"requiresSeller":true}"""), Pasos(), []);

        d.Declaraciones.Select(x => (x.Clave, x.ActivadaPor, x.Obligatoria)).Should().Equal(
            ("simultaneo_color", "CAMBIO_COLOR", false),
            ("simultaneo_combustible", "CONVERSION_COMBUSTIBLE", false),
            ("simultaneo_carroceria", "CAMBIO_CARROCERIA", false));
        d.Flags.PermiteTransformaciones.Should().BeTrue();
    }

    [Theory]
    [InlineData("LEVANTAMIENTO_PRENDA", "levantar")]
    [InlineData("PRENDA_INSCRIPCION", "registrar")]
    [InlineData("TRASPASO_STANDARD", "")]
    [InlineData("CAMBIO_COLOR", "")]
    public void FlagAccionPrenda_SoloEnLosTiposPrendariosDeAccionUnica(string codigo, string esperada) =>
        PersonaNaturalDefinicionMapper.Armar(Tipo(codigo, "OTROS", PerfilOtros), Pasos(), []).Flags.AccionPrenda.Should().Be(esperada);

    [Fact]
    public void LaHuella_CambiaConLasDeclaraciones()
    {
        var conDeclaracion = PersonaNaturalDefinicionMapper.Armar(Tipo("CAMBIO_COLOR", "OTROS", PerfilOtros), Pasos(), []);
        var sinDeclaracion = PersonaNaturalDefinicionMapper.Armar(Tipo("DUPLICADO_TARJETA", "OTROS", PerfilOtros), Pasos(), []);

        conDeclaracion.Huella.Should().HaveLength(64).And.NotBe(sinDeclaracion.Huella);
        PersonaNaturalDefinicionMapper.Huella(conDeclaracion).Should().Be(conDeclaracion.Huella, "la huella es estable sobre el mismo contenido");
    }

    [Fact]
    public void Leasing_LaDefinicionTraeSoloAlLocatario()
    {
        var d = PersonaNaturalDefinicionMapper.Armar(
            Tipo("MATRICULA_LEASING", "MATRICULAS", """{"entryMode":"VIN","requiresBuyer":true,"requiresLessee":true,"biometricActors":["BUYER"],"requiresBiometrics":true}"""), Pasos(), [Doc("contrato_leasing")]);

        d.Actores.Select(a => a.Rol).Should().Equal("locatario");
        d.Documentos.Should().Contain(x => x.DocCode == "contrato_leasing");
    }
}
