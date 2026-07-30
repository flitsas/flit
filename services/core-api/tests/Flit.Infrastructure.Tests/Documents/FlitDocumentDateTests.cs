using Flit.Infrastructure.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #11049 — formato de fecha de los documentos generados: <b>AÑO/MES/DÍA sin hora</b>. Cubre los dos
/// casos: fechas propias del sistema (tipadas) y fechas que llegan como TEXTO del proveedor (SOAT, RTM,
/// RUES), donde lo importante es no perder ni inventar el dato cuando no se puede interpretar.
/// </summary>
public sealed class FlitDocumentDateTests
{
    [Fact]
    public void FechaDelSistema_SeImprimeSinHora()
    {
        var momento = new DateTimeOffset(2026, 7, 29, 15, 42, 11, TimeSpan.Zero);

        FlitDocumentDate.Format(momento).Should().Be("2026/07/29");
    }

    [Fact]
    public void FechaDelSistemaComoDateTime_SeImprimeSinHora()
    {
        FlitDocumentDate.Format(new DateTime(2026, 1, 5, 23, 59, 0, DateTimeKind.Utc))
            .Should().Be("2026/01/05");
    }

    // Formatos que entregan los proveedores: ISO con y sin hora, día-primero y barra.
    [Theory]
    [InlineData("2026-07-29", "2026/07/29")]
    [InlineData("2026-07-29 15:42", "2026/07/29")]
    [InlineData("2026-07-29 15:42:11", "2026/07/29")]
    [InlineData("2026-07-29T15:42:11", "2026/07/29")]
    [InlineData("2026-07-29T15:42:11Z", "2026/07/29")]
    [InlineData("29/07/2026", "2026/07/29")]
    [InlineData("29/07/2026 15:42", "2026/07/29")]
    [InlineData("29-07-2026", "2026/07/29")]
    [InlineData("2026/07/29", "2026/07/29")]
    public void FechaDelProveedor_SeNormalizaAlFormatoDocumental(string entrada, string esperado)
    {
        FlitDocumentDate.Normalize(entrada).Should().Be(esperado);
    }

    // Día primero, no mes: 12/07 es 12 de julio (proveedores colombianos), no 7 de diciembre.
    [Fact]
    public void FechaAmbigua_SeInterpretaConElDiaPrimero()
    {
        FlitDocumentDate.Normalize("12/07/2026").Should().Be("2026/07/12");
    }

    [Fact]
    public void ValorNoInterpretable_SeImprimeTalCual()
    {
        // Un certificado externo puede traer texto libre: preferimos mostrarlo a perderlo.
        FlitDocumentDate.Normalize("SIN INFORMACIÓN").Should().Be("SIN INFORMACIÓN");
        FlitDocumentDate.Normalize("N/A").Should().Be("N/A");
    }

    [Fact]
    public void ValorAusente_SeDevuelveIgual()
    {
        FlitDocumentDate.Normalize(null).Should().BeNull();
        FlitDocumentDate.Normalize("").Should().BeEmpty();
        FlitDocumentDate.Normalize("   ").Should().Be("   ");
    }

    [Fact]
    public void ElResultadoNormalizadoNuncaLlevaHora()
    {
        foreach (var entrada in new[]
                 {
                     "2026-07-29 15:42:11", "29/07/2026 08:00", "2026-07-29T23:59:59Z",
                 })
        {
            FlitDocumentDate.Normalize(entrada).Should().NotContain(":", $"entrada '{entrada}'");
        }
    }
}
