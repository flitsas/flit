using FluentAssertions;
using Flit.Consultas.Api.Impuesto;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto;

/// <summary>C3-2 — día/mes y el mes en palabras. Una fecha imposible no se inventa.</summary>
public sealed class LectorFechaImpuestoTests
{
    [Theory]
    [InlineData("09/02/2025", "2025-02-09")]
    [InlineData("09-02-2025", "2025-02-09")]
    [InlineData("Febrero 9 de 2025", "2025-02-09")]
    [InlineData("2026-02-04", "2026-02-04")]
    public void Lee_los_formatos_vistos(string texto, string esperado) =>
        LectorFechaImpuesto.Leer(texto).Should().Be(esperado);

    [Theory]
    [InlineData("31/02/2025")]
    [InlineData("texto libre")]
    [InlineData("")]
    public void Una_fecha_imposible_queda_sin_dato(string texto) =>
        LectorFechaImpuesto.Leer(texto).Should().BeNull();

    [Theory]
    [InlineData("2025", 2025)]
    [InlineData(" 2024 ", 2024)]
    [InlineData("", null)]
    [InlineData("vigencia", null)]
    public void El_anio_es_entero_o_nada(string texto, int? esperado) =>
        LectorFechaImpuesto.Anio(texto).Should().Be(esperado);
}
