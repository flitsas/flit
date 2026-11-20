using System.Text;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Infrastructure.Documents;
using Flit.Infrastructure.Documents.Reports;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// Correos 2.0 — el Resumen Ejecutivo y los Reportes 2.0 salen en Carta con el membrete FLIT (antes A4/Arial). Una tabla de más
/// de <see cref="TabularReportPdfGenerator.MaxPortraitColumns"/> columnas pasa el reporte a Carta horizontal para que los
/// encabezados no se partan a mitad de palabra.
/// </summary>
public sealed class ReportPdfLetterheadTests
{
    private const string CartaVertical = "/MediaBox [0 0 612 792]";
    private const string CartaHorizontal = "/MediaBox [0 0 792 612]";

    [Fact]
    public void Resumen_ejecutivo_sale_en_carta_vertical()
    {
        var data = new ExecutiveSummaryData(
            Guid.NewGuid(), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
            [new CategoryMetricsDto("matriculas", 3, [new StatusCountDto("aprobado", 3)])],
            [new TopProducerDto(Guid.NewGuid(), "Ana", 3, 3, 0)]);

        MediaBoxes(new ExecutiveSummaryPdfGenerator().Generate(data)).Should().OnlyContain(m => m == CartaVertical);
    }

    [Fact]
    public void Reporte_con_tablas_angostas_sale_en_carta_vertical()
    {
        var bytes = TabularReportPdfGenerator.Generate("Informe programado FLIT", "Informe de revisores", "2026-06-01 a 2026-06-30",
            [new TabularReportPdfGenerator.Section("Revisores", ["Revisor", "Decididos", "Aprobación (%)", "Tiempo mediano (h)"], [["Ana", "3", "100", "2"]])]);

        MediaBoxes(bytes).Should().OnlyContain(m => m == CartaVertical);
    }

    [Fact]
    public void Reporte_con_una_tabla_de_8_columnas_sale_en_carta_horizontal()
    {
        var bytes = TabularReportPdfGenerator.Generate("Informe programado FLIT", "Organismo de Tránsito", "2026-06-01 a 2026-06-30",
        [
            new TabularReportPdfGenerator.Section("Resumen",
                ["Entregados", "Aprobados", "Rechazados", "Tasa rechazo (%)", "P50 horas", "P90 horas", "Reincidencia (%)", "Atascados"],
                [["1", "1", "0", "0", "1", "1", "0", "0"]]),
            new TabularReportPdfGenerator.Section("Atascados", ["Referencia", "Estado", "Días", "Organismo"], []),
        ]);

        MediaBoxes(bytes).Should().OnlyContain(m => m == CartaHorizontal);
    }

    [Theory]
    [InlineData(6, CartaVertical)]
    [InlineData(7, CartaHorizontal)]
    public void El_limite_es_de_seis_columnas_en_vertical(int columnas, string esperado)
    {
        var encabezados = Enumerable.Range(1, columnas).Select(i => $"Columna {i}").ToList();
        var bytes = TabularReportPdfGenerator.Generate("Informe programado FLIT", "Uso", "2026-06-01 a 2026-06-30",
            [new TabularReportPdfGenerator.Section("Tabla", encabezados, [encabezados])]);

        MediaBoxes(bytes).Should().OnlyContain(m => m == esperado);
    }

    private static List<string> MediaBoxes(byte[] pdf)
    {
        var texto = Encoding.Latin1.GetString(pdf);
        var cajas = System.Text.RegularExpressions.Regex.Matches(texto, @"/MediaBox \[[^\]]*\]").Select(m => m.Value).ToList();
        cajas.Should().NotBeEmpty();
        return cajas;
    }
}
