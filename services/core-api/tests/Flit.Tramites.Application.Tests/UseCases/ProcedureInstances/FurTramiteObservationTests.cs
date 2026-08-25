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
            "Matrícula con locatario por Leasing de BANCO LEASING S.A. a LOCATARIO TIPO DE DOCUMENTO CC, NÚMERO DE DOCUMENTO 10203040");
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
}
