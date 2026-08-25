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

    [Fact]
    public void OtroTipo_NoInventaBloque()
    {
        FurTramiteObservation.Compose("MATRICULA_NUEVA",
            [new DocumentParte("comprador", "ANA", "1", null, "CC")]).Should().BeNull();
    }
}
