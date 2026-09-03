using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class FurTramiteObservationTests
{
    [Fact]
    public void Leasing_ConcatenaPropietarioYLocatario()
    {
        var texto = FurTramiteObservation.Compose("MATRICULA_LEASING",
        [
            new DocumentParte("comprador", "BANCO LEASING S.A.", "800111222", null, "NIT"),
            new DocumentParte("locatario", "ANA LOCATARIA", "10203040", null, "CC"),
        ]);

        texto.Should().Be(
            "Matrícula con locatario por Leasing de BANCO LEASING S.A. a ANA LOCATARIA TIPO DE DOCUMENTO CC, NÚMERO DE DOCUMENTO 10203040");
    }

    [Fact]
    public void Unilateral_DeclaraLocatario()
    {
        var texto = FurTramiteObservation.Compose("TRASPASO_UNILATERAL",
        [
            new DocumentParte("vendedor", "PROPIETARIO LEASING", "800111222", null, "NIT"),
            new DocumentParte("comprador", "ANA LOCATARIA", "10203040", null, "CC"),
        ]);

        texto.Should().Be(
            "Traspaso unilateral por leasing a ANA LOCATARIA., tipo de documento CC, número de documento 10203040.");
    }

    [Fact]
    public void Leasing_SinLocatario_SeQuedaSinSuBloqueObligatorio()
    {
        // Este era el estado REAL de producción antes de que el arrendatario fuera capturable:
        // `ParteRol` no tenía `Locatario`, así que ningún actor podía persistirse con ese rol, la
        // composición caía al comprador, detectaba que propietario y locatario eran la misma parte y
        // callaba. Callar es lo correcto —imprimir «de X a X» sería peor—, pero el efecto es que la
        // matrícula por leasing emitía el FUR SIN la observación que el artefacto marca como
        // obligatoria (docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md, tabla 1).
        //
        // Se conserva como prueba porque el caso sigue siendo alcanzable: un expediente al que
        // todavía no se le ha llenado el paso del locatario. Lo que cambió es que ahora HAY paso.
        FurTramiteObservation.Compose("MATRICULA_LEASING",
            [new DocumentParte("comprador", "BANCO LEASING S.A.", "800111222", null, "NIT")])
            .Should().BeNull();
    }

    /// <summary>
    /// Frase acordada con negocio para la casilla 18: quién deja de ser arrendatario y quién pasa a
    /// serlo. Se compara la cadena COMPLETA, no un fragmento: el texto se imprime en el formulario y
    /// una coma o una mayúscula de más ya no es lo pactado.
    /// </summary>
    [Fact]
    public void CambioLocatario_NombraAlPropietarioYAlLocatarioQueEntra()
    {
        var texto = FurTramiteObservation.Compose("CAMBIO_LOCATARIO",
        [
            new DocumentParte("comprador", "Renting Colombia S.A.S", "900123456", null, "NIT"),
            new DocumentParte("locatario", "WILLYN LONDOÑO LONDOÑO", "1037669356", null, "CC"),
        ]);

        texto.Should().Be(
            "CAMBIO DE LOCATARIO por Leasing de Renting Colombia S.A.S a WILLYN LONDOÑO LONDOÑO, "
            + "TIPO DE DOCUMENTO CC, NÚMERO DE DOCUMENTO 1037669356.");
    }

    [Fact]
    public void CambioLocatario_ElPropietarioVaSoloConSuNombre()
    {
        // El NIT del propietario está capturado y aun así NO se imprime: el tipo y el número de
        // documento acompañan solo al locatario, que es la parte que entra.
        FurTramiteObservation.Compose("CAMBIO_LOCATARIO",
        [
            new DocumentParte("comprador", "Renting Colombia S.A.S", "900123456", null, "NIT"),
            new DocumentParte("locatario", "ANA LOCATARIA", "10203040", null, "CC"),
        ])!.Should().NotContain("900123456");
    }

    [Fact]
    public void CambioLocatario_SinLocatario_NoCaeAlCompradorNiSeInventa()
    {
        // Leasing y unilateral sí caen al comprador cuando falta el locatario. Aquí NO: el trámite es
        // el cambio de una parte por otra, y con una sola la frase diría que alguien se sustituye a sí
        // mismo. Regla del artefacto: faltan datos ⇒ sí casilla, sí tipo, no se inventa el texto.
        FurTramiteObservation.Compose("CAMBIO_LOCATARIO",
            [new DocumentParte("comprador", "Renting Colombia S.A.S", "900123456", null, "NIT")])
            .Should().BeNull();
    }

    [Fact]
    public void CambioLocatario_SinPropietario_NoSeInventa()
    {
        FurTramiteObservation.Compose("CAMBIO_LOCATARIO",
            [new DocumentParte("locatario", "ANA LOCATARIA", "10203040", null, "CC")])
            .Should().BeNull();
    }

    [Fact]
    public void CambioLocatario_SinDocumentoDelLocatario_MarcaElHuecoYNoOmiteLaFrase()
    {
        var texto = FurTramiteObservation.Compose("CAMBIO_LOCATARIO",
        [
            new DocumentParte("comprador", "Renting Colombia S.A.S", "900123456", null, "NIT"),
            new DocumentParte("locatario", "ANA LOCATARIA", "   ", null, "   "),
        ]);

        texto.Should().Be(
            "CAMBIO DE LOCATARIO por Leasing de Renting Colombia S.A.S a ANA LOCATARIA, "
            + "TIPO DE DOCUMENTO -, NÚMERO DE DOCUMENTO -.");
    }

    [Fact]
    public void OtroTipo_NoInventaBloque()
    {
        FurTramiteObservation.Compose("MATRICULA_NUEVA",
            [new DocumentParte("comprador", "ANA", "1", null, "CC")]).Should().BeNull();
    }

    // ── Cancelación de matrícula: la causal (tabla 5 del artefacto) ───────────

    [Theory]
    [InlineData("DECISION_JUDICIAL", "CANCELACIÓN POR DECISIÓN JUDICIAL.")]
    [InlineData("PERDIDA_TOTAL_FUERZA_MAYOR", "CANCELACIÓN POR PÉRDIDA TOTAL - FUERZA MAYOR.")]
    [InlineData("PERDIDA_TOTAL_ACCIDENTE", "CANCELACIÓN POR PÉRDIDA TOTAL - ACCIDENTE.")]
    [InlineData("DECISION_VOLUNTARIA", "CANCELACIÓN POR DECISIÓN VOLUNTARIA.")]
    public void Cancelacion_DeclaraLaCausalDeclarada(string causal, string esperado)
    {
        FurTramiteObservation.Compose("CANCELACION_MATRICULA", [], new FurTramiteObservationContext(CancelacionCausal: causal))
            .Should().Be(esperado, "la casilla 13 no distingue una causal de otra");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PERDIDA_TOTAL")]
    public void Cancelacion_SinCausalDeclarada_NoInventaMotivo(string? causal)
    {
        // Regla del artefacto: faltan datos ⇒ sí casilla, no texto. Escribir una causal por defecto
        // declararía ante el organismo un motivo que nadie eligió — y de él cuelgan los documentos
        // con los que el trámite se acredita.
        FurTramiteObservation.Compose("CANCELACION_MATRICULA", [], new FurTramiteObservationContext(CancelacionCausal: causal)).Should().BeNull();
    }

    [Fact]
    public void Cancelacion_LaCausalSoloLaMiraSuTipo()
    {
        FurTramiteObservation.Compose("DUPLICADO_PLACA", [], new FurTramiteObservationContext(CancelacionCausal: "DECISION_JUDICIAL")).Should().BeNull();
    }

    // ── Radicado de cuenta ───────────────────────────────────────────────────────────────────

    [Fact]
    public void RadicadoCuenta_DeclaraElOrganismoDeDestino()
    {
        // El encabezado del FUR lleva el organismo donde el vehículo está matriculado HOY, así que
        // sin esta línea el formulario no dice a dónde va la cuenta — que es el trámite entero.
        FurTramiteObservation.Compose("RADICADO_CUENTA", [], new FurTramiteObservationContext(OrganismoDestino: "Secretaría de Tránsito de Envigado"))
            .Should().Be("Radicado de cuenta en SECRETARÍA DE TRÁNSITO DE ENVIGADO");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RadicadoCuenta_SinDestino_NoInventaElTexto(string? destino)
    {
        // Regla del artefacto: faltan datos ⇒ sí casilla y sí tipo, no se inventa el bloque.
        FurTramiteObservation.Compose("RADICADO_CUENTA", [], new FurTramiteObservationContext(OrganismoDestino: destino))
            .Should().BeNull();
    }

    [Fact]
    public void OtroTipo_ConDestino_NoLoImprime()
    {
        FurTramiteObservation.Compose("CAMBIO_COLOR", [], new FurTramiteObservationContext(OrganismoDestino: "OT ENVIGADO"))
            .Should().BeNull();
    }

    // ── Traslado de cuenta: el ESPEJO del radicado ───────────────────────────────────────────

    [Fact]
    public void TrasladoCuenta_DeclaraPlacaYDestino()
    {
        // El traslado lo expide el organismo de ORIGEN —él valida el paz y salvo y da salida a la
        // cuenta— así que el encabezado del FUR lleva el de origen y el destino solo cabe aquí.
        FurTramiteObservation.Compose(
            "TRASLADO_CUENTA", [],
            new FurTramiteObservationContext(
                OrganismoDestino: "Secretaría de Tránsito de Envigado", Placa: "abc123"))
            .Should().Be(
                "Traslado de cuenta del Vehículo con placa ABC123 para la nueva secretaria de "
                + "SECRETARÍA DE TRÁNSITO DE ENVIGADO");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TrasladoCuenta_SinDestino_NoInventaElTexto(string? destino)
    {
        FurTramiteObservation.Compose(
            "TRASLADO_CUENTA", [], new FurTramiteObservationContext(OrganismoDestino: destino, Placa: "ABC123"))
            .Should().BeNull();
    }

    [Fact]
    public void TrasladoCuenta_SinPlaca_NoOmiteElBloque()
    {
        // La placa la trae siempre el expediente (el trámite entra por placa); si faltara, el destino
        // sigue siendo lo que el organismo necesita leer.
        FurTramiteObservation.Compose(
            "TRASLADO_CUENTA", [], new FurTramiteObservationContext(OrganismoDestino: "OT ENVIGADO"))
            .Should().Be("Traslado de cuenta del Vehículo con placa - para la nueva secretaria de OT ENVIGADO");
    }

    [Fact]
    public void TrasladoYRadicado_NoComparten_Literal()
    {
        // Son los dos tiempos del mismo movimiento y el organismo distingue uno de otro por el texto.
        var ctx = new FurTramiteObservationContext(OrganismoDestino: "OT ENVIGADO", Placa: "ABC123");

        FurTramiteObservation.Compose("TRASLADO_CUENTA", [], ctx)
            .Should().NotBe(FurTramiteObservation.Compose("RADICADO_CUENTA", [], ctx));
    }
}