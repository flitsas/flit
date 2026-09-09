using Flit.Admin.Domain.GeneracionDocumental;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// HU #12208 — catálogo de las condiciones especiales de traspaso del anexo normativo §4.0
/// (<c>docs/plantilla-transferencia-dominio.md</c>), que es la matriz de bloqueo de VB-07.
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// TransferSpecialRegime.Find("VEHICULO_BLINDADO")!.Articulo; // "art. 5.3.2.6"
/// </code>
/// </summary>
public sealed class TransferSpecialRegimeTests
{
    /// <summary>Son ONCE, ni diez ni doce: arts. 5.3.2.3 a 5.3.2.13, uno por condición.</summary>
    [Fact]
    public void ElCatalogoTieneExactamenteOnceCondiciones()
    {
        TransferSpecialRegime.All.Should().HaveCount(11);
        TransferSpecialRegime.All.Select(c => c.Codigo).Should().OnlyHaveUniqueItems();
    }

    /// <summary>Cada condición con su artículo, en el orden de la tabla del anexo §4.0.</summary>
    [Fact]
    public void CadaCondicionLlevaSuArticuloEnElOrdenDelAnexo()
    {
        TransferSpecialRegime.All.Select(c => c.Articulo).Should().Equal(
            "art. 5.3.2.3",
            "art. 5.3.2.4",
            "art. 5.3.2.5",
            "art. 5.3.2.6",
            "art. 5.3.2.7",
            "art. 5.3.2.8",
            "art. 5.3.2.9",
            "art. 5.3.2.10",
            "art. 5.3.2.11",
            "art. 5.3.2.12",
            "art. 5.3.2.13");
    }

    /// <summary>
    /// El <b>art. 5.3.2.14</b> (expedición de la nueva licencia de tránsito) NO es una condición
    /// especial: es el paso final común a todo traspaso y el anexo lo dice expresamente en su nota
    /// de alcance. Si entrara en esta matriz, bloquearía absolutamente todos los traspasos.
    /// </summary>
    [Fact]
    public void ElArticulo5_3_2_14_NoEstaEnElCatalogo()
    {
        TransferSpecialRegime.All.Should().NotContain(c => c.Articulo.Contains("5.3.2.14"));
        TransferSpecialRegime.All.Should().NotContain(c => c.Articulo.Contains("5.3.2.15"));
        TransferSpecialRegime.IsKnown("ART_5_3_2_14").Should().BeFalse();
        TransferSpecialRegime.IsKnown("EXPEDICION_NUEVA_LICENCIA").Should().BeFalse();
    }

    [Theory]
    [InlineData("VEHICULO_BLINDADO", "art. 5.3.2.6")]
    [InlineData("vehiculo_blindado", "art. 5.3.2.6")]
    [InlineData("  Sucesion  ", "art. 5.3.2.8")]
    public void FindNormalizaElCodigo(string codigo, string articulo)
    {
        TransferSpecialRegime.Find(codigo)!.Articulo.Should().Be(articulo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CONDICION_INVENTADA")]
    public void UnCodigoDesconocidoNoEsCondicionEspecial(string? codigo)
    {
        TransferSpecialRegime.Find(codigo).Should().BeNull();
        TransferSpecialRegime.IsKnown(codigo).Should().BeFalse();
    }

    /// <summary>Ninguna condición se queda sin enunciado: el mensaje de VB-07 lo cita.</summary>
    [Fact]
    public void TodasLasCondicionesTienenTitulo()
    {
        TransferSpecialRegime.All.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Titulo));
    }

    /// <summary>Las tres causales de opción de compra del escenario B, y solo esas (VB-B-03).</summary>
    [Theory]
    [InlineData("EJERCIDA", true)]
    [InlineData("AUTOMATICA", true)]
    [InlineData("TERMINACION_CONTRATO", true)]
    [InlineData("PACTADA", false)]
    [InlineData(null, false)]
    public void CatalogoDeOpcionDeCompra(string? opcion, bool conocida)
    {
        TransferPurchaseOption.IsKnown(opcion).Should().Be(conocida);
    }

    /// <summary>
    /// Parágrafo 1.º del art. 5.3.2.2: con la opción automática basta el contrato de leasing; en los
    /// otros dos casos se aporta además la declaración.
    /// </summary>
    [Fact]
    public void SoloLaOpcionAutomaticaSeConformaConElContrato()
    {
        TransferPurchaseOption.RequiereDeclaracionAdicional(TransferPurchaseOption.Automatica)
            .Should().BeFalse();
        TransferPurchaseOption.RequiereDeclaracionAdicional(TransferPurchaseOption.Ejercida)
            .Should().BeTrue();
        TransferPurchaseOption.RequiereDeclaracionAdicional(TransferPurchaseOption.TerminacionContrato)
            .Should().BeTrue();
    }
}
