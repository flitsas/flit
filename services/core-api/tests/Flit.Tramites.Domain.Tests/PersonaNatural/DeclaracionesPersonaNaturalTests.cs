using Flit.Tramites.Domain.Tramites.PersonaNatural;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.PersonaNatural;

/// <summary>HU 4 — las declaraciones salen de las mismas constantes y reglas que el asistente de FLIT 2.</summary>
public sealed class DeclaracionesPersonaNaturalTests
{
    private static ProcedureTypeGateProfile Perfil(string json = "{}") => ProcedureTypeGateProfile.FromJson(json);

    private const string PerfilOtros =
        """{"entryMode":"PLATE","requiresBuyer":true,"allowsComplementaryPrenda":false,"allowsComplementaryTransformations":false}""";

    [Theory]
    [InlineData("CAMBIO_COLOR", "transformacion_color", "vehicle_color", "cambio_color", "soporte_cambio_color", "colores")]
    [InlineData("CONVERSION_COMBUSTIBLE", "transformacion_combustible", "vehicle_fuel", "cambio_combustible", "soporte_conversion_combustible", "combustibles")]
    [InlineData("CAMBIO_CARROCERIA", "transformacion_carroceria", "vehicle_body_type", "cambio_carroceria", "factura_carroceria", "carrocerias")]
    public void UnTipoDeTransformacion_DeclaraSuAtributo_ObligatorioYConSoporteOpcional(
        string tipo, string clave, string campo, string bandera, string soporte, string catalogo)
    {
        var d = DeclaracionesPersonaNatural.Para(Perfil(PerfilOtros), "OTROS", tipo).Should().ContainSingle().Which;

        d.Clave.Should().Be(clave);
        d.Control.Should().Be("seleccion_catalogo");
        d.Obligatoria.Should().BeTrue();
        d.FieldKey.Should().Be(campo);
        d.FieldKeysExtra.Should().Equal(bandera);
        d.DocCodeSoporte.Should().Be(soporte);
        d.SoporteObligatorio.Should().BeFalse("en FLIT 2 el soporte de la transformación es opcional");
        d.Catalogo.Should().Be(catalogo);
        d.ActivadaPor.Should().BeEmpty();
    }

    [Fact]
    public void Combustibles_TraeLasOpcionesDeFlit2()
    {
        var d = DeclaracionesPersonaNatural.Para(Perfil(PerfilOtros), "OTROS", "CONVERSION_COMBUSTIBLE").Single();

        d.Opciones.Select(o => o.Codigo).Should().Equal(
            "GASOLINA", "DIESEL", "GAS NATURAL", "ELECTRICO", "HIBRIDO", "HIDROGENO", "ETANOL", "BIODIESEL", "MIXTO", "OTRO");
        DeclaracionesPersonaNatural.Combustibles.Should().HaveCount(10);
    }

    [Fact]
    public void Blindaje_OfreceLosNivelesYElDesmonte_ConCertificadoObligatorioEnTodas()
    {
        var d = DeclaracionesPersonaNatural.Para(Perfil(PerfilOtros), "OTROS", "BLINDAJE").Should().ContainSingle().Which;

        d.Control.Should().Be("seleccion");
        d.FieldKey.Should().Be("blindaje_nivel");
        d.FieldKeysExtra.Should().Equal("blindaje");
        d.Opciones.Select(o => o.Codigo).Should().Equal(BlindajeOpciones.Codigos);
        d.Opciones.Should().OnlyContain(o => o.ExigeSoporte && o.DocCodes.SequenceEqual(new[] { "certificado_blindaje" }));
        d.SoporteObligatorio.Should().BeTrue();
        d.DocCodeSoporte.Should().Be("certificado_blindaje");
    }

    [Fact]
    public void CancelacionDeMatricula_OfreceLasCuatroCausales_ConLosDocumentosDeCadaUna()
    {
        var d = DeclaracionesPersonaNatural.Para(Perfil(PerfilOtros), "MATRICULAS", CancelacionCausales.TipoCodigo).Should().ContainSingle().Which;

        d.FieldKey.Should().Be("cancelacion_causal");
        d.Opciones.Select(o => o.Codigo).Should().Equal(CancelacionCausales.Codigos);
        d.Opciones.Single(o => o.Codigo == "DECISION_JUDICIAL").DocCodes.Should().Equal("oficio_judicial");
        d.Opciones.Single(o => o.Codigo == "DECISION_VOLUNTARIA").DocCodes.Should().Equal("certificado_dijin");
        d.Opciones.Single(o => o.Codigo == "PERDIDA_TOTAL_ACCIDENTE").DocCodes.Should().BeEquivalentTo(
            "certificado_dijin", "certificado_aseguradora_perito", "certificado_autoridad_administrativa");
        d.Opciones.Single(o => o.Codigo == "PERDIDA_TOTAL_FUERZA_MAYOR").Etiqueta.Should().Be("Pérdida total por fuerza mayor");
    }

    [Fact]
    public void TrasladoDeCuenta_DeclaraElOrganismoDeDestino()
    {
        var perfil = Perfil("""{"requiresBuyer":true,"requiresDestinationTransitOffice":true,"allowsComplementaryTransformations":false}""");

        var d = DeclaracionesPersonaNatural.Para(perfil, "OTROS", "TRASLADO_CUENTA").Should().ContainSingle().Which;

        d.Control.Should().Be("organismo_destino");
        d.Obligatoria.Should().BeTrue();
        d.FieldKey.Should().Be("transit_office_destino_id");
        d.FieldKeysExtra.Should().Equal("transit_office_destino_code", "transit_office_destino_name", "transit_office_destino_city");
    }

    [Fact]
    public void MatriculaYTraspaso_OfrecenLasTransformacionesSimultaneas_OpcionalesYActivadasPorLaTransformacion()
    {
        var lista = DeclaracionesPersonaNatural.Para(Perfil("""{"requiresBuyer":true,"requiresSeller":true}"""), "TRASPASO", "TRASPASO_STANDARD");

        lista.Select(d => d.Clave).Should().Equal("simultaneo_color", "simultaneo_combustible", "simultaneo_carroceria");
        lista.Should().OnlyContain(d => !d.Obligatoria);
        lista.Select(d => d.ActivadaPor).Should().Equal("CAMBIO_COLOR", "CONVERSION_COMBUSTIBLE", "CAMBIO_CARROCERIA");
    }

    [Fact]
    public void UnTipoQueNoAcumulaNiDeclaraNada_NoTieneDeclaraciones()
    {
        DeclaracionesPersonaNatural.Para(Perfil(PerfilOtros), "OTROS", "DUPLICADO_TARJETA").Should().BeEmpty();
        DeclaracionesPersonaNatural.Para(Perfil(PerfilOtros), "OTROS", "LEVANTAMIENTO_PRENDA").Should().BeEmpty();
    }

    [Fact]
    public void DocumentosDeDeclaraciones_JuntaSoportesYDocumentosPorOpcion()
    {
        var docs = DeclaracionesPersonaNatural.DocumentosDeDeclaraciones(
            DeclaracionesPersonaNatural.Para(Perfil(PerfilOtros), "MATRICULAS", CancelacionCausales.TipoCodigo));

        docs.Should().BeEquivalentTo(new[] { "oficio_judicial", "certificado_dijin", "certificado_aseguradora_perito", "certificado_autoridad_administrativa" });
    }

    [Fact]
    public void Derivados_DelBlindaje_ElDesmonteDejaLaBanderaEnFalso_YLosNivelesEnVerdadero()
    {
        var perfil = Perfil(PerfilOtros);

        Banderas(perfil, "BLINDAJE", ("blindaje_nivel", "NIVEL_2")).Should().Equal(("blindaje", "true"));
        Banderas(perfil, "BLINDAJE", ("blindaje_nivel", "DESMONTE")).Should().Equal(("blindaje", "false"));
        Banderas(perfil, "BLINDAJE").Should().BeEmpty("sin opción declarada no se deriva nada");
    }

    [Fact]
    public void Derivados_DeUnaTransformacionBase_PrendenLaBanderaCambioSoloConValor()
    {
        var perfil = Perfil(PerfilOtros);

        Banderas(perfil, "CAMBIO_COLOR", ("vehicle_color", "ROJO")).Should().Equal(("cambio_color", "true"));
        Banderas(perfil, "CAMBIO_COLOR", ("vehicle_color", "  ")).Should().BeEmpty();
        Banderas(perfil, "CAMBIO_CARROCERIA", ("vehicle_color", "ROJO")).Should().BeEmpty("el color no es lo que declara este tipo");
    }

    [Fact]
    public void Derivados_NoTocanLosSimultaneos_PorqueLosActivaElCiudadano()
    {
        var perfil = Perfil("""{"requiresBuyer":true,"requiresSeller":true}""");

        DeclaracionesPersonaNatural.Derivados(perfil, "TRASPASO", "TRASPASO_STANDARD", new Dictionary<string, string> { ["vehicle_color"] = "ROJO" })
            .Should().BeEmpty();
    }

    private static IEnumerable<(string, string)> Banderas(ProcedureTypeGateProfile perfil, string tipo, params (string Clave, string Valor)[] campos) =>
        DeclaracionesPersonaNatural
            .Derivados(perfil, "OTROS", tipo, campos.ToDictionary(c => c.Clave, c => c.Valor))
            .Select(kv => (kv.Key, kv.Value));
}
