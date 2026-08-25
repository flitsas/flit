using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// Opción de blindaje: los cuatro códigos del trámite y la bandera que se DERIVA de ellos, que es la
/// que acaba marcando la casilla «vehículo blindado SI/NO» del FUR.
/// </summary>
public sealed class BlindajeOpcionTests
{
    [Theory]
    [InlineData("NIVEL_1", BlindajeOpcion.Nivel1)]
    [InlineData("NIVEL_2", BlindajeOpcion.Nivel2)]
    [InlineData("NIVEL_3", BlindajeOpcion.Nivel3)]
    [InlineData("DESMONTE", BlindajeOpcion.Desmonte)]
    public void Parse_ReconoceLosCuatroCodigos(string valor, BlindajeOpcion esperada)
    {
        BlindajeOpciones.Parse(valor).Should().Be(esperada);
    }

    [Theory]
    [InlineData("  nivel_2  ")]
    [InlineData("Nivel_2")]
    public void Parse_NormalizaEspaciosYMayusculas(string valor)
    {
        // El valor viaja en field_values, que es texto libre: un espacio de más no puede cambiar lo
        // que el formulario declara.
        BlindajeOpciones.Parse(valor).Should().Be(BlindajeOpcion.Nivel2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NIVEL_4")]
    [InlineData("true")]
    public void Parse_ValorDesconocidoNoSeAdivina(string? valor)
    {
        // Ninguna ⇒ el FUR marca casilla pero NO inventa el texto. Adivinar un nivel aquí sería
        // declarar ante el organismo un blindaje que nadie eligió.
        BlindajeOpciones.Parse(valor).Should().Be(BlindajeOpcion.Ninguna);
    }

    [Theory]
    [InlineData(BlindajeOpcion.Nivel1, true)]
    [InlineData(BlindajeOpcion.Nivel2, true)]
    [InlineData(BlindajeOpcion.Nivel3, true)]
    [InlineData(BlindajeOpcion.Desmonte, false)]
    [InlineData(BlindajeOpcion.Ninguna, false)]
    public void DejaElVehiculoBlindado_SoloLosNiveles(BlindajeOpcion opcion, bool esperado)
    {
        // El desmonte es un trámite de blindaje que deja el vehículo SIN blindaje: es la razón de que
        // la bandera se derive de la opción en vez de copiarse del tipo.
        BlindajeOpciones.DejaElVehiculoBlindado(opcion).Should().Be(esperado);
    }

    [Fact]
    public void Codigos_ExponeLasCuatroOpcionesEnOrden()
    {
        BlindajeOpciones.Codigos.Should().Equal("NIVEL_1", "NIVEL_2", "NIVEL_3", "DESMONTE");
    }

    [Theory]
    [InlineData(BlindajeOpcion.Nivel1, "NIVEL_1")]
    [InlineData(BlindajeOpcion.Desmonte, "DESMONTE")]
    [InlineData(BlindajeOpcion.Ninguna, null)]
    public void ToCodigo_EsInversaDeParse(BlindajeOpcion opcion, string? esperado)
    {
        BlindajeOpciones.ToCodigo(opcion).Should().Be(esperado);
    }
}
