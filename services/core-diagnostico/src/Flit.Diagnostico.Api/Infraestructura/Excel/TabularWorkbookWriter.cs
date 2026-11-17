using System.IO.Compression;
using System.Xml;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Flit.Diagnostico.Api.Infraestructura.Excel;

/// <summary>
/// Copia de <c>services/core-comparendos/src/Flit.Comparendos.Api/Infraestructura/Excel/TabularWorkbookWriter.cs</c> (a su vez copia de
/// <c>Flit.Infrastructure.Documents.Reports.TabularWorkbookWriter</c> de core-api) para que el Excel del lote de Diagnóstico (HU #13849)
/// se vea igual que los del resto de FLIT: mismas 5 celdas tipadas y estilos, encabezado en negrita sobre tinta FLIT, panel congelado,
/// anchos y autofiltro. Cambios de esta copia: (1) escribe en memoria (sin archivo temporal: el libro lleva documentos enmascarados y
/// datos de la compañía); (2) protección contra inyección de fórmulas: toda celda de texto que empiece por <c>=</c>, <c>+</c>, <c>-</c>,
/// <c>@</c>, tabulador o retorno de carro se escribe como texto literal (texto en línea, nunca fórmula, con el estilo
/// <c>quotePrefix</c> y formato <c>@</c>, así Excel no la evalúa ni al editarla). El texto no cambia: una celda manual y una de API
/// siguen produciendo lo mismo.
/// </summary>
/// <example>
/// <code>
/// var bytes = TabularWorkbookWriter.Write([new Sheet("Vehículos", [new Column("Placa")], [[Cell.Of("TST401")]])]);
/// </code>
/// </example>
internal static class TabularWorkbookWriter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Índices de <c>cellXfs</c> en el styles.xml embebido — el orden aquí y el de esa
    /// constante tienen que coincidir. Mismos 5 estilos que <c>frontend/lib/xlsx.ts</c>.</summary>
    private static class StyleIndex
    {
        public const uint Normal = 0;
        public const uint Header = 1;
        public const uint Fecha = 2;
        public const uint FechaHora = 3;
        public const uint Decimal = 4;
        public const uint TextoLiteral = 5;
        public const uint Vinculo = 6;
    }

    /// <summary>Primeros caracteres con los que una hoja de cálculo interpreta el texto como fórmula (OWASP CSV/Formula Injection).</summary>
    private static readonly char[] InicioDeFormula = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>¿Lo interpretaría Excel (o LibreOffice) como fórmula si se escribiera tal cual?</summary>
    public static bool PareceFormula(string? valor) => !string.IsNullOrEmpty(valor) && Array.IndexOf(InicioDeFormula, valor[0]) >= 0;

    /// <summary>Una celda tipada: string, número, fecha (con u sin hora) o vacía. El llamador
    /// decide el tipo — este escritor no adivina a partir de un string, porque «100» como texto y
    /// 100 como número se ven distintos en Excel (alineación, si se puede sumar).</summary>
    public abstract record Cell
    {
        private Cell() { }

        public sealed record Str(string Value) : Cell;
        public sealed record Num(double Value) : Cell;
        public sealed record Fecha(DateOnly Value) : Cell;
        public sealed record FechaHora(DateOnly Value, int Hour, int Minute) : Cell;
        public sealed record Vacia : Cell;

        public static readonly Cell Empty = new Vacia();

        public static Cell Of(string? value) =>
            string.IsNullOrEmpty(value) ? Empty : new Str(value);

        public static Cell Of(int value) => new Num(value);

        public static Cell Of(double value) => new Num(value);

        public static Cell Of(DateOnly value) => new Fecha(value);

        public static Cell OfDateTime(DateOnly date, int hour, int minute) => new FechaHora(date, hour, minute);
    }

    public sealed record Column(string Header, int Width = 16);

    public sealed record Sheet(string Name, IReadOnlyList<Column> Columns, IReadOnlyList<IReadOnlyList<Cell>> Rows)
    {
        /// <summary>Atajo para hojas de puro texto (Uso del aplicativo, OT): sin números ni fechas
        /// tipadas que aprovechar, cada valor ya viene formateado a la unidad que le corresponde.</summary>
        public static Sheet OfText(string name, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows) =>
            new(
                name,
                headers.Select(h => new Column(h)).ToList(),
                rows.Select(row => (IReadOnlyList<Cell>)row.Select(Cell.Of).ToList()).ToList());
    }

    public static byte[] Write(IReadOnlyList<Sheet> sheets)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        if (sheets.Count == 0)
            throw new ArgumentException("Se requiere al menos una hoja.", nameof(sheets));

        using var memoria = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(memoria, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var sheetsElement = workbookPart.Workbook.AppendChild(new Sheets());

            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            using (var stylesStream = stylesPart.GetStream(FileMode.Create, FileAccess.Write))
            using (var stylesWriter = new StreamWriter(stylesStream, new UTF8Encoding(false)))
                stylesWriter.Write(StylesXml);

            uint sheetId = 1;
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sheet in sheets)
            {
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                WriteWorksheet(worksheetPart, sheet);

                sheetsElement.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = sheetId++,
                    Name = UniqueSheetName(sheet.Name, usedNames),
                });
            }

            workbookPart.Workbook.Save();
        }

        return memoria.ToArray();
    }

    private static void WriteWorksheet(WorksheetPart worksheetPart, Sheet sheet)
    {
        using var writer = OpenXmlWriter.Create(worksheetPart);
        writer.WriteStartElement(new Worksheet());

        // Panel congelado bajo la fila 1: un informe de cientos de filas sin encabezado fijo
        // obliga a subir cada vez para recordar qué columna se está mirando.
        writer.WriteStartElement(new SheetViews());
        writer.WriteStartElement(new SheetView { WorkbookViewId = 0U });
        writer.WriteElement(new Pane
        {
            VerticalSplit = 1D,
            TopLeftCell = "A2",
            ActivePane = PaneValues.BottomLeft,
            State = PaneStateValues.Frozen,
        });
        writer.WriteEndElement(); // SheetView
        writer.WriteEndElement(); // SheetViews

        writer.WriteStartElement(new Columns());
        for (var i = 0; i < sheet.Columns.Count; i++)
        {
            writer.WriteElement(new DocumentFormat.OpenXml.Spreadsheet.Column
            {
                Min = (uint)(i + 1),
                Max = (uint)(i + 1),
                Width = sheet.Columns[i].Width,
                CustomWidth = true,
            });
        }

        writer.WriteEndElement(); // Columns

        writer.WriteStartElement(new SheetData());
        WriteHeaderRow(writer, sheet.Columns);
        for (var r = 0; r < sheet.Rows.Count; r++)
            WriteRow(writer, sheet.Rows[r], r + 2);
        writer.WriteEndElement(); // SheetData

        // El filtro estándar de columna: sin esto, cada quien que abre el archivo tiene que
        // armarlo a mano antes de poder ordenar o filtrar una sola columna.
        var lastCol = ColumnName(Math.Max(0, sheet.Columns.Count - 1));
        var lastRow = sheet.Rows.Count + 1;
        writer.WriteElement(new AutoFilter { Reference = $"A1:{lastCol}{lastRow}" });

        writer.WriteEndElement(); // Worksheet
        writer.Close();
    }

    private static void WriteHeaderRow(OpenXmlWriter writer, IReadOnlyList<Column> columns)
    {
        writer.WriteStartElement(new Row());
        foreach (var column in columns)
        {
            var cell = new DocumentFormat.OpenXml.Spreadsheet.Cell
            {
                DataType = CellValues.InlineString,
                StyleIndex = StyleIndex.Header,
            };
            cell.AppendChild(new InlineString(new Text(column.Header) { Space = SpaceProcessingModeValues.Preserve }));
            writer.WriteElement(cell);
        }

        writer.WriteEndElement();
    }

    private static void WriteRow(OpenXmlWriter writer, IReadOnlyList<Cell> values, int rowNumber)
    {
        writer.WriteStartElement(new Row { RowIndex = (uint)rowNumber });
        foreach (var value in values)
            writer.WriteElement(ToOpenXmlCell(value));
        writer.WriteEndElement();
    }

    private static DocumentFormat.OpenXml.Spreadsheet.Cell ToOpenXmlCell(Cell value) => value switch
    {
        Cell.Vacia => new DocumentFormat.OpenXml.Spreadsheet.Cell(),
        Cell.Str s => StringCell(s.Value),
        // Los enteros van sin formato de decimales: «12,00 devoluciones» pide leer dos decimales
        // que no existen.
        Cell.Num n => new DocumentFormat.OpenXml.Spreadsheet.Cell
        {
            StyleIndex = n.Value == Math.Floor(n.Value) ? StyleIndex.Normal : StyleIndex.Decimal,
            CellValue = new CellValue(n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        },
        Cell.Fecha f => new DocumentFormat.OpenXml.Spreadsheet.Cell
        {
            StyleIndex = StyleIndex.Fecha,
            CellValue = new CellValue(ExcelSerial(f.Value, 0, 0)
                .ToString(System.Globalization.CultureInfo.InvariantCulture)),
        },
        Cell.FechaHora fh => new DocumentFormat.OpenXml.Spreadsheet.Cell
        {
            StyleIndex = StyleIndex.FechaHora,
            CellValue = new CellValue(ExcelSerial(fh.Value, fh.Hour, fh.Minute)
                .ToString(System.Globalization.CultureInfo.InvariantCulture)),
        },
        _ => new DocumentFormat.OpenXml.Spreadsheet.Cell(),
    };

    private static DocumentFormat.OpenXml.Spreadsheet.Cell StringCell(string value)
    {
        // Texto en línea: nunca CellFormula. Si parece fórmula, además quotePrefix + formato «@» (texto literal al editarla).
        var cell = new DocumentFormat.OpenXml.Spreadsheet.Cell { DataType = CellValues.InlineString };
        if (PareceFormula(value))
            cell.StyleIndex = StyleIndex.TextoLiteral;
        cell.AppendChild(new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve }));
        return cell;
    }

    /// <summary>
    /// Libro de una sola hoja escrito en streaming (ajustes 2026-11-11, punto 8: índice del ZIP masivo). A diferencia de
    /// <see cref="Write"/>, no junta las filas: arma el paquete .xlsx con un <see cref="ZipArchive"/> en modo Create sobre
    /// <c>destino</c> (sirve la entrada de otro ZIP, que no admite Seek; el SDK no abre paquetes de solo escritura) y cada
    /// <see cref="Fila"/> sale al destino con el SAX de <see cref="OpenXmlWriter"/>. Mismos estilos, celdas tipadas y protección contra
    /// fórmulas que <see cref="Write"/>; además admite filas previas al encabezado (el panel congelado y el autofiltro arrancan en el
    /// encabezado) y un vínculo relativo por fila (relación externa: abre el archivo hermano donde se descomprima el ZIP). En memoria
    /// solo quedan la referencia y el destino de cada vínculo.
    /// </summary>
    /// <example>
    /// <code>
    /// using (var hoja = TabularWorkbookWriter.HojaEnStreaming.Abrir(destino, "Índice", [[Cell.Of("Lote")]], [new Column("Placa")]))
    ///     hoja.Fila([Cell.Of("TST801")], vinculo: (0, "TST801.pdf"));
    /// </code>
    /// </example>
    public sealed class HojaEnStreaming : IDisposable
    {
        private const string NsPrincipal = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string NsRelaciones = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string NsPaquete = "http://schemas.openxmlformats.org/package/2006/relationships";
        private const string TipoVinculo = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";

        private readonly ZipArchive _paquete;
        private readonly Stream _parteHoja;
        private readonly OpenXmlWriter _writer;
        private readonly string _nombre;
        private readonly int _columnas;
        private readonly int _filaEncabezado;
        private readonly List<(string Referencia, string Ruta)> _vinculos = [];
        private int _ultimaFila;
        private bool _cerrada;

        private HojaEnStreaming(Stream destino, string nombre, IReadOnlyList<IReadOnlyList<Cell>> previas, IReadOnlyList<Column> columnas)
        {
            _nombre = UniqueSheetName(nombre, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            _columnas = columnas.Count;
            _filaEncabezado = previas.Count + 1;
            _paquete = new ZipArchive(destino, ZipArchiveMode.Create, leaveOpen: true);
            _parteHoja = _paquete.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal).Open();
            _writer = OpenXmlWriter.Create(_parteHoja, Encoding.UTF8);
            _writer.WriteStartDocument(true);
            _writer.WriteStartElement(new Worksheet());
            _writer.WriteStartElement(new SheetViews());
            _writer.WriteStartElement(new SheetView { WorkbookViewId = 0U });
            _writer.WriteElement(new Pane
            {
                VerticalSplit = _filaEncabezado,
                TopLeftCell = $"A{_filaEncabezado + 1}",
                ActivePane = PaneValues.BottomLeft,
                State = PaneStateValues.Frozen,
            });
            _writer.WriteEndElement(); // SheetView
            _writer.WriteEndElement(); // SheetViews
            _writer.WriteStartElement(new Columns());
            for (var i = 0; i < columnas.Count; i++)
            {
                _writer.WriteElement(new DocumentFormat.OpenXml.Spreadsheet.Column
                {
                    Min = (uint)(i + 1),
                    Max = (uint)(i + 1),
                    Width = columnas[i].Width,
                    CustomWidth = true,
                });
            }

            _writer.WriteEndElement(); // Columns
            _writer.WriteStartElement(new SheetData());
            foreach (var previa in previas)
                Escribir(previa, null);

            _writer.WriteStartElement(new Row { RowIndex = (uint)_filaEncabezado });
            for (var i = 0; i < columnas.Count; i++)
            {
                var celda = new DocumentFormat.OpenXml.Spreadsheet.Cell
                {
                    CellReference = $"{ColumnName(i)}{_filaEncabezado}",
                    DataType = CellValues.InlineString,
                    StyleIndex = StyleIndex.Header,
                };
                celda.AppendChild(new InlineString(new Text(columnas[i].Header) { Space = SpaceProcessingModeValues.Preserve }));
                _writer.WriteElement(celda);
            }

            _writer.WriteEndElement();
            _ultimaFila = _filaEncabezado;
        }

        /// <summary>
        /// Abre el libro sobre <paramref name="destino"/> (no lo cierra) con las filas <paramref name="previas"/> arriba (una fila vacía
        /// es una lista vacía) y el encabezado de <paramref name="columnas"/> debajo.
        /// </summary>
        public static HojaEnStreaming Abrir(Stream destino, string nombre, IReadOnlyList<IReadOnlyList<Cell>> previas, IReadOnlyList<Column> columnas)
        {
            ArgumentNullException.ThrowIfNull(destino);
            ArgumentNullException.ThrowIfNull(previas);
            ArgumentNullException.ThrowIfNull(columnas);
            if (columnas.Count == 0)
                throw new ArgumentException("Se requiere al menos una columna.", nameof(columnas));
            return new HojaEnStreaming(destino, nombre, previas, columnas);
        }

        /// <summary>
        /// Escribe una fila de datos; <paramref name="vinculo"/> (columna base 0 y ruta relativa) convierte esa celda en vínculo al
        /// archivo hermano. Una ruta vacía no lleva vínculo.
        /// </summary>
        public void Fila(IReadOnlyList<Cell> valores, (int Columna, string? Ruta)? vinculo = null)
        {
            ArgumentNullException.ThrowIfNull(valores);
            ObjectDisposedException.ThrowIf(_cerrada, this);
            Escribir(valores, vinculo is { Ruta.Length: > 0 } v ? v : null);
        }

        private void Escribir(IReadOnlyList<Cell> valores, (int Columna, string? Ruta)? vinculo)
        {
            var fila = ++_ultimaFila;
            _writer.WriteStartElement(new Row { RowIndex = (uint)fila });
            for (var i = 0; i < valores.Count; i++)
            {
                var celda = ToOpenXmlCell(valores[i]);
                celda.CellReference = $"{ColumnName(i)}{fila}";
                if (vinculo is { } v && v.Columna == i)
                {
                    var ruta = Uri.IsWellFormedUriString(v.Ruta, UriKind.Relative) ? v.Ruta! : Uri.EscapeDataString(v.Ruta!);
                    _vinculos.Add((celda.CellReference.Value!, ruta));
                    celda.StyleIndex = StyleIndex.Vinculo;
                }

                _writer.WriteElement(celda);
            }

            _writer.WriteEndElement();
        }

        /// <summary>
        /// Cierra la hoja (autofiltro del encabezado a la última fila y vínculos) y las demás partes del paquete (libro, estilos,
        /// relaciones y tipos de contenido); el destino sigue abierto.
        /// </summary>
        public void Dispose()
        {
            if (_cerrada)
                return;
            _cerrada = true;
            _writer.WriteEndElement(); // SheetData
            _writer.WriteElement(new AutoFilter { Reference = $"A{_filaEncabezado}:{ColumnName(Math.Max(0, _columnas - 1))}{_ultimaFila}" });
            if (_vinculos.Count > 0)
            {
                _writer.WriteStartElement(new Hyperlinks());
                for (var i = 0; i < _vinculos.Count; i++)
                    _writer.WriteElement(new Hyperlink { Reference = _vinculos[i].Referencia, Id = Relacion(i) });
                _writer.WriteEndElement();
            }

            _writer.WriteEndElement(); // Worksheet
            _writer.Close();
            _writer.Dispose();
            _parteHoja.Dispose();

            if (_vinculos.Count > 0)
            {
                Parte("xl/worksheets/_rels/sheet1.xml.rels", x =>
                {
                    x.WriteStartElement("Relationships", NsPaquete);
                    for (var i = 0; i < _vinculos.Count; i++)
                        Relacion(x, Relacion(i), TipoVinculo, _vinculos[i].Ruta, externa: true);
                    x.WriteEndElement();
                });
            }

            Parte("xl/workbook.xml", x =>
            {
                x.WriteStartElement("workbook", NsPrincipal);
                x.WriteAttributeString("xmlns", "r", null, NsRelaciones);
                x.WriteStartElement("sheets", NsPrincipal);
                x.WriteStartElement("sheet", NsPrincipal);
                x.WriteAttributeString("name", _nombre);
                x.WriteAttributeString("sheetId", "1");
                x.WriteAttributeString("id", NsRelaciones, "rId1");
                x.WriteEndElement();
                x.WriteEndElement();
                x.WriteEndElement();
            });
            Parte("xl/_rels/workbook.xml.rels", x =>
            {
                x.WriteStartElement("Relationships", NsPaquete);
                Relacion(x, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet1.xml", externa: false);
                Relacion(x, "rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml", externa: false);
                x.WriteEndElement();
            });
            using (var estilos = _paquete.CreateEntry("xl/styles.xml", CompressionLevel.Optimal).Open())
            using (var escritor = new StreamWriter(estilos, new UTF8Encoding(false)))
                escritor.Write(StylesXml);
            Parte("_rels/.rels", x =>
            {
                x.WriteStartElement("Relationships", NsPaquete);
                Relacion(x, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml", externa: false);
                x.WriteEndElement();
            });
            Parte("[Content_Types].xml", x =>
            {
                const string ns = "http://schemas.openxmlformats.org/package/2006/content-types";
                x.WriteStartElement("Types", ns);
                Defecto(x, ns, "rels", "application/vnd.openxmlformats-package.relationships+xml");
                Defecto(x, ns, "xml", "application/xml");
                Sobrescribe(x, ns, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                Sobrescribe(x, ns, "/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                Sobrescribe(x, ns, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
                x.WriteEndElement();
            });
            _paquete.Dispose();
        }

        private static string Relacion(int vinculo) => $"rIdV{(vinculo + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        private void Parte(string nombre, Action<XmlWriter> escribir)
        {
            using var flujo = _paquete.CreateEntry(nombre, CompressionLevel.Optimal).Open();
            using var x = XmlWriter.Create(flujo, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
            x.WriteStartDocument(true);
            escribir(x);
            x.WriteEndDocument();
        }

        private static void Relacion(XmlWriter x, string id, string tipo, string destino, bool externa)
        {
            x.WriteStartElement("Relationship", NsPaquete);
            x.WriteAttributeString("Id", id);
            x.WriteAttributeString("Type", tipo);
            x.WriteAttributeString("Target", destino);
            if (externa)
                x.WriteAttributeString("TargetMode", "External");
            x.WriteEndElement();
        }

        private static void Defecto(XmlWriter x, string ns, string extension, string tipo)
        {
            x.WriteStartElement("Default", ns);
            x.WriteAttributeString("Extension", extension);
            x.WriteAttributeString("ContentType", tipo);
            x.WriteEndElement();
        }

        private static void Sobrescribe(XmlWriter x, string ns, string parte, string tipo)
        {
            x.WriteStartElement("Override", ns);
            x.WriteAttributeString("PartName", parte);
            x.WriteAttributeString("ContentType", tipo);
            x.WriteEndElement();
        }
    }

    /// <summary>Número de serie de Excel: días desde el 30/12/1899, con la hora como fracción —
    /// misma cuenta que <c>frontend/lib/xlsx.ts#serialOf</c> (el mismo dato tiene que verse
    /// idéntico en el export manual y en el adjunto del correo).</summary>
    private static double ExcelSerial(DateOnly date, int hour, int minute)
    {
        var epoch = new DateOnly(1899, 12, 30);
        var days = date.DayNumber - epoch.DayNumber;
        var seconds = hour * 3600 + minute * 60;
        return days + seconds / 86_400.0;
    }

    /// <summary>Referencia de columna: 0 → A, 25 → Z, 26 → AA.</summary>
    private static string ColumnName(int index)
    {
        var name = string.Empty;
        var n = index;
        while (true)
        {
            name = (char)('A' + (n % 26)) + name;
            if (n < 26)
                return name;
            n = (n / 26) - 1;
        }
    }

    /// <summary>Excel exige nombres de hoja únicos, ≤ 31 caracteres y sin <c>: \ / ? * [ ]</c>.</summary>
    private static string UniqueSheetName(string name, HashSet<string> used)
    {
        var sanitized = new string(name.Select(c => "\\/?*[]:".Contains(c) ? '-' : c).ToArray()).Trim();
        if (sanitized.Length == 0)
            sanitized = "Hoja";
        if (sanitized.Length > 31)
            sanitized = sanitized[..31];

        var candidate = sanitized;
        var suffix = 2;
        while (!used.Add(candidate))
        {
            var marker = $" ({suffix++})";
            candidate = sanitized[..Math.Min(sanitized.Length, 31 - marker.Length)] + marker;
        }

        return candidate;
    }

    /// <summary>
    /// Mismo styles.xml (5 estilos, mismos índices) que <c>frontend/lib/xlsx.ts#STYLES_XML</c>:
    /// encabezado en negrita blanca sobre fondo #162744 (tinta FLIT), y formatos de fecha/fecha-hora/
    /// decimal para que Excel trate esas columnas como lo que son, no como texto. Esta copia agrega el
    /// índice 5: texto literal (formato «@» y quotePrefix) para las celdas que parecen fórmula, y el 6: vínculo (subrayado azul) de
    /// <see cref="HojaEnStreaming"/>.
    /// </summary>
    private const string StylesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<numFmts count=\"3\">" +
        "<numFmt numFmtId=\"164\" formatCode=\"dd/mm/yyyy\"/>" +
        "<numFmt numFmtId=\"165\" formatCode=\"dd/mm/yyyy\\ hh:mm\"/>" +
        "<numFmt numFmtId=\"166\" formatCode=\"#,##0.00\"/>" +
        "</numFmts>" +
        "<fonts count=\"3\">" +
        "<font><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "<font><b/><color rgb=\"FFFFFFFF\"/><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "<font><u/><sz val=\"11\"/><color rgb=\"FF0563C1\"/><name val=\"Calibri\"/></font>" +
        "</fonts>" +
        "<fills count=\"3\">" +
        "<fill><patternFill patternType=\"none\"/></fill>" +
        "<fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF162744\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "</fills>" +
        "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"7\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"166\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"49\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" quotePrefix=\"1\"/>" +
        "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
        "</cellXfs>" +
        "</styleSheet>";
}
