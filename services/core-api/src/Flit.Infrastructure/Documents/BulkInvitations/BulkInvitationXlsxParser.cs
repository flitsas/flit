using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Infrastructure.Documents.BulkTramites;
using Flit.Modules.Security.Application.Auth.BulkInvitation;

namespace Flit.Infrastructure.Documents.BulkInvitations;

/// <summary>
/// Lector del archivo de invitación masiva (HU #14422, Feature #14420, ADR-0077) sobre <c>DocumentFormat.OpenXml</c> crudo en
/// modo SAX — mismo enfoque que <c>BulkTramitesXlsxParser</c>, sin ClosedXML/EPPlus.
///
/// <para><b>Defensas</b> (el archivo es del usuario y no es de fiar):</para>
/// <list type="bullet">
/// <item>Tamaño: se corta la lectura al pasar <see cref="BulkInvitationOptions.MaxFileBytes"/> (<c>FILE_TOO_LARGE</c>) sin
/// cargar el resto en memoria.</item>
/// <item>Zip-bomb: antes de abrir el paquete se suma el tamaño DESCOMPRIMIDO declarado de sus partes y se limita su cantidad
/// (<c>INVALID_FILE</c>).</item>
/// <item>Filas: más de <see cref="BulkInvitationOptions.MaxRows"/> filas de datos rechaza el archivo completo
/// (<c>TOO_MANY_ROWS</c>); no se procesa ninguna.</item>
/// <item>Fórmulas: un valor que empieza por <c>= + - @</c> —o una celda que ES una fórmula— se neutraliza (prefijo <c>'</c>) y
/// la fila se marca para rechazo; el parser nunca evalúa nada.</item>
/// </list>
/// Las columnas se resuelven del atributo <c>r</c> de cada celda (Excel omite las vacías), y se resuelven los tres orígenes
/// de texto (cadena compartida, inline, resultado de fórmula).
/// </summary>
internal sealed class BulkInvitationXlsxParser(BulkInvitationOptions options) : IBulkInvitationXlsxParser
{
    private const int MaxZipEntries = 200;

    // Filas físicas que se recorren como máximo (incluye las vacías con formato): evita que un archivo con cientos de miles
    // de <row/> vacíos consuma CPU aunque sus datos sean pocos.
    private const int MaxScannedRows = 10_000;

    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    public BulkInvitationParseResult Parse(Stream xlsx)
    {
        ArgumentNullException.ThrowIfNull(xlsx);

        if (xlsx.CanSeek)
        {
            if (xlsx.Length > options.MaxFileBytes)
            {
                return BulkInvitationParseResult.Rejected(BulkInvitationFileErrors.FileTooLarge);
            }

            xlsx.Position = 0;
        }

        using var buffer = new MemoryStream();
        if (!TryCopyWithLimit(xlsx, buffer, options.MaxFileBytes))
        {
            return BulkInvitationParseResult.Rejected(BulkInvitationFileErrors.FileTooLarge);
        }

        buffer.Position = 0;
        if (!HasZipSignature(buffer))
        {
            return BulkInvitationParseResult.Rejected(BulkInvitationFileErrors.InvalidFile);
        }

        buffer.Position = 0;
        if (!IsPackageSizeSane(buffer))
        {
            return BulkInvitationParseResult.Rejected(BulkInvitationFileErrors.InvalidFile);
        }

        buffer.Position = 0;
        try
        {
            using var document = SpreadsheetDocument.Open(buffer, isEditable: false);
            return Read(document);
        }
#pragma warning disable CA1031 // Cualquier fallo de apertura es «el archivo del usuario no sirve».
        catch (Exception)
#pragma warning restore CA1031
        {
            return BulkInvitationParseResult.Rejected(BulkInvitationFileErrors.InvalidFile);
        }
    }

    private static bool TryCopyWithLimit(Stream source, MemoryStream destination, int maxBytes)
    {
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (destination.Length + read > maxBytes)
            {
                return false;
            }

            destination.Write(chunk, 0, read);
        }

        return true;
    }

    private static bool HasZipSignature(MemoryStream buffer)
    {
        Span<byte> head = stackalloc byte[4];
        return buffer.Length >= 4
            && buffer.Read(head) == 4
            && head.SequenceEqual(ZipSignature);
    }

    private bool IsPackageSizeSane(MemoryStream buffer)
    {
        try
        {
            using var zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > MaxZipEntries)
            {
                return false;
            }

            long total = 0;
            foreach (var entry in zip.Entries)
            {
                total += entry.Length;
                if (total > options.MaxUncompressedBytes)
                {
                    return false;
                }
            }

            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private BulkInvitationParseResult Read(SpreadsheetDocument document)
    {
        var workbookPart = document.WorkbookPart;
        if (workbookPart is null)
        {
            return BulkInvitationParseResult.Rejected(BulkInvitationFileErrors.InvalidFile);
        }

        var sheet = workbookPart.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault();
        if (sheet?.Id?.Value is null
            || workbookPart.GetPartById(sheet.Id.Value) is not WorksheetPart worksheetPart)
        {
            return BulkInvitationParseResult.Rejected(BulkInvitationFileErrors.InvalidFile);
        }

        var sharedStrings = ReadSharedStrings(workbookPart);
        var rows = new List<BulkInvitationParsedRow>();
        var headerRead = false;
        var scanned = 0;

        using var reader = OpenXmlReader.Create(worksheetPart);

        while (reader.Read())
        {
            if (reader.ElementType != typeof(Row) || !reader.IsStartElement)
            {
                continue;
            }

            if (++scanned > MaxScannedRows)
            {
                return BulkInvitationParseResult.Rejected(BulkInvitationFileErrors.InvalidFile);
            }

            var row = (Row)reader.LoadCurrentElement()!;
            var cells = ReadCells(row, sharedStrings);

            if (!headerRead)
            {
                if (!HeaderMatches(cells))
                {
                    return BulkInvitationParseResult.Rejected(BulkInvitationFileErrors.TemplateInvalid);
                }

                headerRead = true;
                continue;
            }

            if (cells.Values.All(c => !c.IsFormula && string.IsNullOrWhiteSpace(c.Text)))
            {
                continue;
            }

            if (rows.Count >= options.MaxRows)
            {
                return BulkInvitationParseResult.Rejected(BulkInvitationFileErrors.TooManyRows);
            }

            var number = (int)(row.RowIndex?.Value ?? 0);
            if (number < 2)
            {
                number = rows.Count + 2;
            }

            rows.Add(ToParsedRow(number, cells));
        }

        if (!headerRead)
        {
            return BulkInvitationParseResult.Rejected(BulkInvitationFileErrors.TemplateInvalid);
        }

        return rows.Count == 0
            ? BulkInvitationParseResult.Rejected(BulkInvitationFileErrors.EmptyFile)
            : BulkInvitationParseResult.Ok(rows);
    }

    private static bool HeaderMatches(Dictionary<int, CellData> cells)
    {
        var expected = BulkInvitationTemplateCatalog.Headers;
        for (var i = 0; i < expected.Count; i++)
        {
            if (!cells.TryGetValue(i, out var cell)
                || !string.Equals(cell.Text?.Trim(), expected[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static BulkInvitationParsedRow ToParsedRow(int number, Dictionary<int, CellData> cells)
    {
        var neutralized = false;

        string Take(int column)
        {
            if (!cells.TryGetValue(column, out var cell))
            {
                return string.Empty;
            }

            var text = cell.Text?.Trim() ?? string.Empty;
            if (cell.IsFormula || BulkInvitationFormulaGuard.IsRisky(text))
            {
                neutralized = true;
                text = BulkInvitationFormulaGuard.Neutralize(text) ?? string.Empty;
            }

            return text;
        }

        var fullName = Take(0);
        var email = Take(1);
        var role = Take(2);

        return new BulkInvitationParsedRow(number, fullName, email, role, neutralized);
    }

    private static List<string> ReadSharedStrings(WorkbookPart workbookPart)
    {
        var values = new List<string>();
        var part = workbookPart.SharedStringTablePart;
        if (part is null)
        {
            return values;
        }

        using var reader = OpenXmlReader.Create(part);
        while (reader.Read())
        {
            if (reader.ElementType == typeof(SharedStringItem) && reader.IsStartElement)
            {
                values.Add(reader.LoadCurrentElement()?.InnerText ?? string.Empty);
            }
        }

        return values;
    }

    private static Dictionary<int, CellData> ReadCells(Row row, List<string> sharedStrings)
    {
        var cells = new Dictionary<int, CellData>();
        var fallback = 0;

        foreach (var cell in row.Elements<Cell>())
        {
            var column = BulkTramitesXlsxParser.ColumnIndex(cell.CellReference?.Value);
            if (column < 0)
            {
                column = fallback;
            }

            fallback = column + 1;
            cells[column] = new CellData(CellText(cell, sharedStrings), cell.CellFormula is not null);
        }

        return cells;
    }

    private static string? CellText(Cell cell, List<string> sharedStrings)
    {
        var type = cell.DataType?.Value;

        if (type == CellValues.SharedString)
        {
            return int.TryParse(cell.CellValue?.Text, out var index) && index >= 0 && index < sharedStrings.Count
                ? sharedStrings[index]
                : null;
        }

        if (type == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText;
        }

        if (type == CellValues.Boolean)
        {
            return cell.CellValue?.Text == "1" ? "TRUE" : "FALSE";
        }

        return cell.CellValue?.Text;
    }

    /// <summary>Texto de una celda y si era una fórmula (<c>&lt;f&gt;</c>), cuyo resultado en caché nunca se confía.</summary>
    private readonly record struct CellData(string? Text, bool IsFormula);
}
