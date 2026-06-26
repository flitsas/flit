using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Genera el Excel del detalle de trámites (HU #10245) con <see cref="OpenXmlWriter"/> en streaming:
/// las filas llegan del lector de BD una a una y se escriben directo al paquete .xlsx, sin
/// materializar el conjunto completo (AC2 — procesamiento por chunks, memoria acotada).
///
/// El paquete OPC (.xlsx = zip) requiere un stream con seek para escribir el directorio central,
/// y el cuerpo de respuesta HTTP no lo es; por eso se escribe a un archivo temporal y luego se
/// copia al <c>output</c>. El temporal mantiene la memoria acotada también para volúmenes grandes.
/// </summary>
internal sealed class ProcedureExcelExporter : IProcedureExcelExporter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly string[] Headers =
        ["Referencia", "Tipo de trámite", "Categoría", "Estado", "Radicado por", "Enviado", "Completado"];

    private readonly IAnalyticsReadRepository _repo;

    public ProcedureExcelExporter(IAnalyticsReadRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public async Task ExportAsync(Stream output, ProcedureExportFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(filter);

        var tempPath = Path.Combine(Path.GetTempPath(), $"flit-analytics-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var document = SpreadsheetDocument.Create(tempPath, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

                using (var writer = OpenXmlWriter.Create(worksheetPart))
                {
                    writer.WriteStartElement(new Worksheet());
                    writer.WriteStartElement(new SheetData());

                    WriteRow(writer, Headers);

                    await _repo.ExportProcedureDetailsAsync(
                        filter.TenantId, filter.From, filter.To, filter.Category, filter.Status,
                        (row, _) =>
                        {
                            WriteRow(writer, ToCells(row));
                            return Task.CompletedTask;
                        },
                        ct).ConfigureAwait(false);

                    writer.WriteEndElement(); // SheetData
                    writer.WriteEndElement(); // Worksheet
                    writer.Close();
                }

                workbookPart.Workbook = new Workbook(
                    new Sheets(new Sheet
                    {
                        Id = workbookPart.GetIdOfPart(worksheetPart),
                        SheetId = 1U,
                        Name = "Trámites",
                    }));
                workbookPart.Workbook.Save();
            }

            await using var file = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None,
                bufferSize: 81920, useAsync: true);
            await file.CopyToAsync(output, ct).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string[] ToCells(ProcedureDetailDto r) =>
    [
        r.ReferenceNumber,
        r.ProcedureTypeName,
        r.Category,
        r.Status,
        r.CreatedByDisplayName,
        r.SubmittedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
        r.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
    ];

    private static void WriteRow(OpenXmlWriter writer, IReadOnlyList<string> values)
    {
        writer.WriteStartElement(new Row());
        foreach (var value in values)
        {
            var cell = new Cell { DataType = CellValues.InlineString };
            cell.AppendChild(new InlineString(new Text(value)));
            writer.WriteElement(cell);
        }

        writer.WriteEndElement();
    }
}
