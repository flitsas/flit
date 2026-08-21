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
    public void OtroTipo_NoInventaBloque()
    {
        FurTramiteObservation.Compose("MATRICULA_NUEVA",
            [new DocumentParte("comprador", "ANA", "1", null, "CC")]).Should().BeNull();
    }
}
