using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Infrastructure.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// Uso de ejemplo:
/// var exporter = new ProcedureExcelExporter(repo);
/// await exporter.ExportAsync(stream, filter, ct);
/// Cubre HU #10245 AC1 (xlsx con columnas obligatorias) y AC3 (sin datos → solo encabezados).
/// </summary>
public sealed class ProcedureExcelExporterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly ProcedureExportFilter Filter =
        new(Guid.NewGuid(), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, null);

    /// <summary>Repo en memoria: empuja las filas dadas al callback de streaming del exporter.</summary>
    private sealed class FakeRepo(IReadOnlyList<ProcedureDetailDto> rows) : IAnalyticsReadRepository
    {
        public Task<IReadOnlyList<CategoryMetricsDto>> GetOverviewAsync(Guid t, DateOnly f, DateOnly to, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<TopProducerDto>> GetTopProducersAsync(Guid t, DateOnly f, DateOnly to, int limit, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<ProcedureDetailsPageDto> GetProcedureDetailsAsync(Guid t, DateOnly f, DateOnly to, string? c, string? s, int page, int pageSize, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public async Task ExportProcedureDetailsAsync(Guid t, DateOnly f, DateOnly to, string? c, string? s,
            Func<ProcedureDetailDto, CancellationToken, Task> onRowAsync, CancellationToken ct = default)
        {
            foreach (var row in rows)
                await onRowAsync(row, ct);
        }
    }

    private static List<Row> ReadRows(Stream xlsx)
    {
        xlsx.Position = 0;
        using var doc = SpreadsheetDocument.Open(xlsx, false);
        var sheetData = doc.WorkbookPart!.WorksheetParts.Single().Worksheet.GetFirstChild<SheetData>()!;
        return sheetData.Elements<Row>().ToList();
    }

    [Fact] // AC1 — xlsx con encabezado y una fila de datos con las columnas obligatorias
    public async Task ExportAsync_ConFilas_EscribeEncabezadoYDatos()
    {
        var rows = new List<ProcedureDetailDto>
        {
            new(Guid.NewGuid(), "REF-1", "Matrícula nueva", "matriculas", "submitted", "Ana",
                DateTimeOffset.UtcNow, null),
        };
        using var ms = new MemoryStream();

        await new ProcedureExcelExporter(new FakeRepo(rows)).ExportAsync(ms, Filter, Ct);

        var sheetRows = ReadRows(ms);
        sheetRows.Should().HaveCount(2); // encabezado + 1 dato
        sheetRows[0].Elements<Cell>().Select(c => c.InnerText).Should()
            .Contain(["Referencia", "Tipo de trámite", "Categoría", "Estado", "Radicado por"]);
        sheetRows[1].Elements<Cell>().Select(c => c.InnerText).Should()
            .Contain(["REF-1", "matriculas", "submitted", "Ana"]);
    }

    [Fact] // AC2 — volumen grande (>1000): el streaming completa sin materializar el conjunto
    public async Task ExportAsync_VolumenGrande_EscribeTodasLasFilas()
    {
        var rows = Enumerable.Range(0, 5000)
            .Select(i => new ProcedureDetailDto(Guid.NewGuid(), $"REF-{i}", "Tipo", "otros", "draft", "Dev", null, null))
            .ToList();
        using var ms = new MemoryStream();

        await new ProcedureExcelExporter(new FakeRepo(rows)).ExportAsync(ms, Filter, Ct);

        ReadRows(ms).Should().HaveCount(5001); // encabezado + 5000
    }

    [Fact] // AC3 — sin datos: solo la fila de encabezados, archivo válido
    public async Task ExportAsync_SinFilas_SoloEncabezados()
    {
        using var ms = new MemoryStream();

        await new ProcedureExcelExporter(new FakeRepo([])).ExportAsync(ms, Filter, Ct);

        var sheetRows = ReadRows(ms);
        sheetRows.Should().ContainSingle();
        sheetRows[0].Elements<Cell>().Should().HaveCount(7);
    }
}
