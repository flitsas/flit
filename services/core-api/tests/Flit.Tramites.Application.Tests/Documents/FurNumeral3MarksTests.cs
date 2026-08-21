using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Documents;

/// <summary>Tablas 1–3 del numeral 3: <c>docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md</c>.</summary>
public sealed class FurNumeral3MarksTests
{
    private static HashSet<int> Marks(
        string code,
        string familia = "OTROS",
        FurPrendaMarking prenda = FurPrendaMarking.Ninguna,
        FurTransformacionesDeclaradas t = default) =>
        FurNumeral3Marks.Resolve(code, familia, prenda, t);

    [Theory]
    [InlineData("MATRICULA_NUEVA", "MATRICULAS", 1)]
    [InlineData("MATRICULA_LEASING", "MATRICULAS", 1)]
    [InlineData("TRASPASO_STANDARD", "TRASPASO", 2)]
    [InlineData("TRASPASO_UNILATERAL", "TRASPASO", 2)]
    [InlineData("TRASPASO_TRANSFERENCIA_DE_DOMINIO", "TRASPASO", 2)]
    [InlineData("TRASLADO_CUENTA", "OTROS", 3)]
    [InlineData("RADICADO_CUENTA", "OTROS", 4)]
    [InlineData("CAMBIO_COLOR", "OTROS", 5)]
    [InlineData("DUPLICADO_TARJETA", "OTROS", 10)]
    [InlineData("PRENDA_INSCRIPCION", "OTROS", 11)]
    [InlineData("LEVANTAMIENTO_PRENDA", "OTROS", 12)]
    [InlineData("CANCELACION_MATRICULA", "OTROS", 13)]
    [InlineData("DUPLICADO_PLACA", "OTROS", 15)]
    [InlineData("REMATRICULA", "OTROS", 16)]
    [InlineData("CAMBIO_CARROCERIA", "OTROS", 17)]
    [InlineData("CONVERSION_COMBUSTIBLE", "OTROS", 18)]
    public void Tabla1_Codigo_MarcaCasillaYNoInventaMatricula(string code, string familia, int casilla)
    {
        var marks = Marks(code, familia);
        marks.Should().Contain(casilla);
        if (casilla != 1)
            marks.Should().NotContain(1);
    }

    [Fact]
    public void Cancelacion_NoMarcaCasilla1()
    {
        Marks("CANCELACION_MATRICULA").Should().Equal(13);
    }

    [Fact]
    public void CambioColor_NoMarcaCasilla1()
    {
        Marks("CAMBIO_COLOR").Should().Equal(5);
    }

    [Fact]
    public void Blindaje_Numeral3Vacio()
    {
        Marks("BLINDAJE").Should().BeEmpty();
    }

    [Fact]
    public void Regrabar_Marca7Y8()
    {
        Marks("REGRABAR_MOTOR_CHASIS").Should().BeEquivalentTo([7, 8]);
    }

    [Fact]
    public void FallbackMatricula_CodigoLegacy()
    {
        Marks("matricula_inicial", "MATRICULAS").Should().Equal(1);
    }

    [Fact]
    public void TipoDesconocido_FamiliaOtros_Vacio()
    {
        Marks("TIPO_INVENTADO", "OTROS").Should().BeEmpty();
    }

    [Fact]
    public void EjemploCerrado_TraspasoConstitucionColorCarroceria()
    {
        var marks = Marks(
            "TRASPASO_STANDARD",
            "TRASPASO",
            FurPrendaMarking.Constitucion,
            new FurTransformacionesDeclaradas(Color: true, Carroceria: true));

        marks.Should().BeEquivalentTo([2, 11, 5, 17]);
    }

    [Fact]
    public void PrendaBase_NoDuplicaComplementaria()
    {
        Marks("PRENDA_INSCRIPCION", prenda: FurPrendaMarking.Constitucion).Should().Equal(11);
    }

    [Fact]
    public void Ambos_Une11Y12SobreTraspaso()
    {
        Marks("TRASPASO_STANDARD", "TRASPASO", FurPrendaMarking.Ambos)
            .Should().BeEquivalentTo([2, 11, 12]);
    }
}
