using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries;

namespace Flit.Infrastructure.Documents;

/// <summary>Export Excel del reporte detallado (HU #10816) — streaming OpenXml.</summary>
internal sealed class DetailedReportExcelExporter : IDetailedReportExcelExporter
{
    public const string ContentType = ProcedureExcelExporter.ContentType;

    private static readonly string[] Headers =
    [
        "Referencia", "Tipo de trámite", "Categoría", "Estado", "Radicado por",
        "Persona documento", "Persona nombre", "Transformación", "Detalle transformación",
        "Leasing", "Tipo pago", "Tipo traspaso", "Enviado", "Completado",
    ];

    private readonly IDetailedReportReadRepository _repo;

    public DetailedReportExcelExporter(IDetailedReportReadRepository repo) =>
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));

    public async Task ExportAsync(Stream output, DetailedReportFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(filter);

        var tempPath = Path.Combine(Path.GetTempPath(), $"flit-detailed-report-{Guid.NewGuid():N}.xlsx");
        var wroteRows = false;
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

                    await _repo.ExportProceduresAsync(
                        filter,
                        (row, token) =>
                        {
                            wroteRows = true;
                            WriteRow(writer, ToCells(row));
                            return Task.CompletedTask;
                        },
                        ct).ConfigureAwait(false);

                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.Close();
                }

                if (!wroteRows)
                    throw new InvalidOperationException("no_records");

                workbookPart.Workbook = new Workbook(
                    new Sheets(new Sheet
                    {
                        Id = workbookPart.GetIdOfPart(worksheetPart),
                        SheetId = 1U,
                        Name = "Reporte detallado",
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
                File.Delete(tempPath);
        }
    }

    private static string[] ToCells(DetailedProcedureRowDto r) =>
    [
        r.ReferenceNumber,
        r.ProcedureTypeName,
        r.Category,
        r.Status,
        r.CreatedByDisplayName,
        r.PersonDocument,
        r.PersonFullName,
        r.HasTransformation ? "Sí" : "No",
        r.TransformationDetail ?? string.Empty,
        r.IsLeasing ? "Sí" : "No",
        r.PaymentType,
        r.TransferType,
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
