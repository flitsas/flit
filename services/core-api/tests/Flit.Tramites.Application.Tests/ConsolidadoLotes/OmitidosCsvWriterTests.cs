using System.Text;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13377 (AC5) — <see cref="OmitidosCsvWriter"/>: UTF-8 con BOM, separador <c>;</c>, columnas
/// <c>radicado;placa;motivo</c> y neutralización de fórmulas con apóstrofo.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var bytes = OmitidosCsvWriter.Generar([new OmitidoCsvFila("R-1", "ABC123", "Acceso revocado")]);
/// </code>
/// </remarks>
public sealed class OmitidosCsvWriterTests
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private static string Texto(byte[] bytes) => new UTF8Encoding(false).GetString(bytes.AsSpan(3));

    [Fact]
    public void AC5_Utf8ConBom_Separador_Y_Columnas()
    {
        var bytes = OmitidosCsvWriter.Generar([new OmitidoCsvFila("R-2026-1", "ABC123", "Acceso revocado")]);

        bytes.AsSpan(0, 3).ToArray().Should().Equal(Bom);
        Texto(bytes).Should().Be("radicado;placa;motivo\r\nR-2026-1;ABC123;Acceso revocado\r\n");
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")")]
    [InlineData("+57")]
    [InlineData("-1+1")]
    [InlineData("@SUM(A1)")]
    public void AC5_Negativo_FormulaEnMotivoOPlaca_SalePrefijadaConApostrofo(string peligroso)
    {
        var texto = Texto(OmitidosCsvWriter.Generar([new OmitidoCsvFila("R-1", peligroso, peligroso)]));

        var fila = texto.Split("\r\n")[1];
        var esperado = OmitidosCsvWriter.Celda(peligroso);
        esperado.TrimStart('"').Should().StartWith("'" + peligroso[0]);
        fila.Should().Be($"R-1;{esperado};{esperado}");
    }

    [Fact]
    public void AC5_Negativo_FormulaEnRadicado_TambienSeNeutraliza()
    {
        OmitidosCsvWriter.Celda("=1+1").Should().Be("'=1+1");
        OmitidosCsvWriter.Celda("\t=1").Should().Be("'\t=1");
    }

    [Fact]
    public void AC5_ValorConSeparadorOComillas_VaEntrecomillado_YNoParteLaFila()
    {
        OmitidosCsvWriter.Celda("a;b").Should().Be("\"a;b\"");
        OmitidosCsvWriter.Celda("di \"no\"").Should().Be("\"di \"\"no\"\"\"");
        OmitidosCsvWriter.Celda("linea1\nlinea2").Should().Be("\"linea1\nlinea2\"");
        OmitidosCsvWriter.Celda("=a;b").Should().Be("\"'=a;b\"");
    }

    [Fact]
    public void AC5_SinFilas_SoloBomYEncabezado()
    {
        var bytes = OmitidosCsvWriter.Generar([]);

        bytes.AsSpan(0, 3).ToArray().Should().Equal(Bom);
        Texto(bytes).Should().Be("radicado;placa;motivo\r\n");
    }

    [Fact]
    public void AC5_NulosYTildes_SeEscribenSinRomperUtf8()
    {
        var texto = Texto(OmitidosCsvWriter.Generar([new OmitidoCsvFila(null, null, "El trámite no tiene FUR; no se pudo generar el consolidado")]));

        texto.Split("\r\n")[1].Should().Be(";;\"El trámite no tiene FUR; no se pudo generar el consolidado\"");
    }

    [Fact]
    public void Contrato_Escribir_EnStreamNoBuscable_PoneElBomUnaVez_YNoLoCierra()
    {
        using var destino = new SoloEscritura();

        OmitidosCsvWriter.Escribir(destino, [new OmitidoCsvFila("R-1", "ABC123", "Acceso revocado")]);

        destino.Cerrado.Should().BeFalse();
        var bytes = destino.Contenido.ToArray();
        bytes.AsSpan(0, 3).ToArray().Should().Equal(Bom);
        bytes.AsSpan(3, 3).ToArray().Should().NotEqual(Bom);
        Encoding.UTF8.GetString(bytes.AsSpan(3)).Should().StartWith(OmitidosCsvWriter.Encabezado);
    }

    /// <summary>Stream sin <c>Seek</c> ni <c>Length</c>, como la entrada de un <c>ZipArchive</c> en creación.</summary>
    private sealed class SoloEscritura : Stream
    {
        public MemoryStream Contenido { get; } = new();

        public bool Cerrado { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Contenido.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            Cerrado = true;
            base.Dispose(disposing);
        }
    }
}
