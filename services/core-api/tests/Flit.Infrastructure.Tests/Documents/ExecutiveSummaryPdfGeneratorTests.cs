using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Infrastructure.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// Uso de ejemplo:
/// var bytes = new ExecutiveSummaryPdfGenerator().Generate(data);
/// Cubre HU #10246 AC1 (genera un PDF válido con totales + Top 5) y robustez con datos vacíos.
/// </summary>
public sealed class ExecutiveSummaryPdfGeneratorTests
{
    // Firma de archivo PDF: "%PDF".
    private static readonly byte[] PdfMagic = [0x25, 0x50, 0x44, 0x46];

    [Fact] // AC1 — con datos: produce un PDF no vacío y bien formado
    public void Generate_ConDatos_ProducePdfValido()
    {
        var data = new ExecutiveSummaryData(
            Guid.NewGuid(), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
            new List<CategoryMetricsDto> { new("matriculas", 12, new List<StatusCountDto>()) },
            new List<TopProducerDto> { new(Guid.NewGuid(), "Ana", 12, 5, 1) });

        var bytes = new ExecutiveSummaryPdfGenerator().Generate(data);

        bytes.Should().NotBeNullOrEmpty();
        bytes.Take(4).Should().Equal(PdfMagic);
    }

    [Fact] // Robustez — sin categorías ni Top: igualmente produce un PDF válido
    public void Generate_SinDatos_ProducePdfValido()
    {
        var data = new ExecutiveSummaryData(
            Guid.NewGuid(), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
            [], []);

        var bytes = new ExecutiveSummaryPdfGenerator().Generate(data);

        bytes.Should().NotBeNullOrEmpty();
        bytes.Take(4).Should().Equal(PdfMagic);
    }

    [Fact] // Ajuste #10246 — KPIs + gráficas: con desglose por estado, vehicular y Top 5 → PDF válido
    public void Generate_ConGraficasYDesgloseEstados_ProducePdfValido()
    {
        var data = new ExecutiveSummaryData(
            Guid.NewGuid(), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
            new List<CategoryMetricsDto>
            {
                new("matriculas", 40, new List<StatusCountDto>
                {
                    new("submitted", 18), new("approved_ot", 14), new("rejected_ot", 4), new("draft", 4),
                }),
                new("traspasos", 25, new List<StatusCountDto> { new("submitted", 15), new("completed", 10) }),
                new("vehicular", 18, new List<StatusCountDto> { new("submitted", 12), new("in_review", 6) }),
                new("otros", 0, new List<StatusCountDto>()),
            },
            new List<TopProducerDto>
            {
                new(Guid.NewGuid(), "Ana Gómez", 30, 20, 3),
                new(Guid.NewGuid(), "Luis Ríos", 18, 9, 2),
                new(Guid.NewGuid(), "María Paz", 7, 4, 1),
            });

        var bytes = new ExecutiveSummaryPdfGenerator().Generate(data);

        bytes.Should().NotBeNullOrEmpty();
        bytes.Take(4).Should().Equal(PdfMagic);
        // Un PDF con KPIs + 3 secciones de gráficas + 2 tablas pesa bastante más que el de solo tablas.
        bytes.Length.Should().BeGreaterThan(3000);
    }
}
