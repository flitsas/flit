using FluentAssertions;
using Flit.Consultas.Api.Impuesto;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto;

/// <summary>C3-2 — el punto y la coma son miles. El vacío no es cero y el negativo no es un monto.</summary>
public sealed class LectorMontoImpuestoTests
{
    [Theory]
    [InlineData("$ 1.234.567 COP", 1234567)]
    [InlineData("$ 1,234,567", 1234567)]
    [InlineData("1.234", 1234)]
    [InlineData("$ 1.234", 1234)]
    [InlineData("1234", 1234)]
    [InlineData("0", 0)]
    public void Lee_enteros_con_separador_de_miles(string texto, long esperado) =>
        LectorMontoImpuesto.Leer(texto).Should().Be(esperado);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.234,56")]
    [InlineData("12.34")]
    [InlineData("1.234,567")]
    [InlineData("-1.234")]
    [InlineData("N/A")]
    public void Lo_que_no_afirma_un_entero_queda_sin_dato(string? texto) =>
        LectorMontoImpuesto.Leer(texto).Should().BeNull();
}
