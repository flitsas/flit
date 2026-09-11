using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Tramites.Application.BulkTramites;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Infrastructure.Documents.BulkTramites;

/// <summary>
/// Genera las plantillas XLSX de carga masiva de trámites (HU #12520). Mismo contrato técnico que
/// <c>StandaloneDocumentXlsxTemplate</c> de Generación Documental: todas las columnas en formato
/// TEXTO (para que Excel no convierta VIN/placa/documentos en número o fecha), la hoja de datos
/// siempre primera (el parser de HU #12522 lee por posición, no por nombre), y los desplegables
/// como ayuda de captura — el parser vuelve a validar en el servidor.
///
/// <para>Los códigos de tipo de trámite de la plantilla «Otros» se excluyen de MATRICULA_NUEVA y
/// TRASPASO_STANDARD porque esos dos tienen su propia plantilla dedicada.</para>
/// </summary>
public sealed class BulkTramitesXlsxTemplate(IProcedureTypeRepository procedureTypeRepository)
    : IBulkTramitesXlsxTemplate
{
    private const string Mimetype =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly string[] CodigosConPlantillaPropia = ["MATRICULA_NUEVA", "TRASPASO_STANDARD"];

    private const uint TextStyleIndex = 1;
    private const uint HeaderStyleIndex = 2;
    private const uint TitleStyleIndex = 3;
    private const uint SectionStyleIndex = 4;
    private const uint BodyStyleIndex = 5;

    private const string PromptTitle = "Cómo se llena";

    public async Task<RenderedBulkTramitesTemplate> BuildAsync(
        BulkTramitesTemplateType tipo, CancellationToken ct = default)
    {
        var columnas = tipo == BulkTramitesTemplateType.Otros
            ? BulkTramitesTemplateCatalog.ColumnsFor(tipo, await ResolveTiposTramiteVigentesAsync(ct).ConfigureAwait(false))
            : BulkTramitesTemplateCatalog.ColumnsFor(tipo);

        using var buffer = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = BuildStylesheet();
            stylesPart.Stylesheet.Save();

            var catalogos = BuildCatalogRanges(columnas);

            var datosPart = workbookPart.AddNewPart<WorksheetPart>();
            datosPart.Worksheet = BuildDataSheet(columnas, catalogos);
            datosPart.Worksheet.Save();

            var guiaPart = workbookPart.AddNewPart<WorksheetPart>();
            guiaPart.Worksheet = BuildGuideSheet(tipo, columnas);
            guiaPart.Worksheet.Save();

            var listasPart = workbookPart.AddNewPart<WorksheetPart>();
            listasPart.Worksheet = BuildListsSheet(catalogos);
            listasPart.Worksheet.Save();

            workbookPart.Workbook.AppendChild(new Sheets(
                new Sheet
                {
                    Id = workbookPart.GetIdOfPart(datosPart),
                    SheetId = 1,
                    Name = BulkTramitesTemplateCatalog.SheetName,
                },
                new Sheet
                {
                    Id = workbookPart.GetIdOfPart(guiaPart),
                    SheetId = 2,
                    Name = BulkTramitesTemplateCatalog.GuideSheetName,
                },
                new Sheet
                {
                    Id = workbookPart.GetIdOfPart(listasPart),
                    SheetId = 3,
                    Name = BulkTramitesTemplateCatalog.ListsSheetName,
                    State = SheetStateValues.Hidden,
                }));

            workbookPart.Workbook.Save();
        }

        var slug = tipo switch
        {
            BulkTramitesTemplateType.Matricula => "matricula",
            BulkTramitesTemplateType.Traspaso => "traspaso",
            BulkTramitesTemplateType.Otros => "otros-tramites",
            _ => tipo.ToString().ToLowerInvariant(),
        };

        return new RenderedBulkTramitesTemplate(
            $"plantilla-carga-masiva-{slug}-{BulkTramitesTemplateCatalog.Version}.xlsx",
            Mimetype,
            buffer.ToArray());
    }

    private async Task<IReadOnlyList<string>> ResolveTiposTramiteVigentesAsync(CancellationToken ct)
    {
        var tipos = await procedureTypeRepository.ListAsync(family: null, publicationStatus: null, ct)
            .ConfigureAwait(false);

        return
        [
            .. tipos
                .Where(t => t.IsActive && !CodigosConPlantillaPropia.Contains(t.Code))
                .Select(t => t.Code)
                .OrderBy(c => c, StringComparer.Ordinal),
        ];
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    // Hoja 1 — datos
    // ════════════════════════════════════════════════════════════════════════════════════════════

    private static Worksheet BuildDataSheet(
        IReadOnlyList<BulkTramitesColumnSpec> columnas, List<CatalogRange> catalogos)
    {
        var vistas = new SheetViews(new SheetView
        {
            WorkbookViewId = 0,
            Pane = new Pane
            {
                VerticalSplit = 1D,
                TopLeftCell = "A2",
                ActivePane = PaneValues.BottomLeft,
                State = PaneStateValues.Frozen,
            },
        });

        var columnasEstilo = new Columns(new Column
        {
            Min = 1,
            Max = (uint)columnas.Count,
            Width = 26,
            CustomWidth = true,
            Style = TextStyleIndex,
        });

        var header = new Row { RowIndex = 1 };
        for (var i = 0; i < columnas.Count; i++)
        {
            header.Append(Cell(ColumnName(i) + "1", columnas[i].Header, HeaderStyleIndex));
        }

        return new Worksheet(vistas, columnasEstilo, new SheetData(header), BuildValidations(columnas, catalogos));
    }

    private static DataValidations BuildValidations(
        IReadOnlyList<BulkTramitesColumnSpec> columnas, List<CatalogRange> catalogos)
    {
        var validaciones = new DataValidations();

        for (var i = 0; i < columnas.Count; i++)
        {
            var columna = columnas[i];
            var letra = ColumnName(i);
            var rango = $"{letra}2:{letra}{BulkTramitesTemplateCatalog.MaxRows + 1}";

            var validacion = new DataValidation
            {
                SequenceOfReferences = new ListValue<StringValue> { InnerText = rango },
                AllowBlank = true,
                ShowInputMessage = true,
                PromptTitle = PromptTitle,
                Prompt = columna.Guia,
            };

            if (columna.Opciones is { Count: > 0 })
            {
                var catalogo = catalogos.First(c => Equal(c.Values, columna.Opciones));

                validacion.Type = DataValidationValues.List;
                validacion.Formula1 = new Formula1(catalogo.Reference);
                validacion.ShowErrorMessage = true;
                validacion.ErrorStyle = DataValidationErrorStyleValues.Stop;
                validacion.ErrorTitle = "Valor no admitido";
                validacion.Error =
                    "Elige uno de los valores de la lista. Si escribes otro, la fila quedará en error "
                    + "con el detalle en el resumen del lote.";
            }
            else
            {
                validacion.Type = DataValidationValues.None;
            }

            validaciones.Append(validacion);
        }

        validaciones.Count = (uint)columnas.Count;
        return validaciones;
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    // Hoja 2 — instrucciones
    // ════════════════════════════════════════════════════════════════════════════════════════════

    private static Worksheet BuildGuideSheet(
        BulkTramitesTemplateType tipo, IReadOnlyList<BulkTramitesColumnSpec> columnas)
    {
        var columnasEstilo = new Columns(
            new Column { Min = 1, Max = 1, Width = 32, CustomWidth = true },
            new Column { Min = 2, Max = 2, Width = 95, CustomWidth = true });

        var datos = new SheetData();
        var fila = 1u;

        void Titulo(string texto) => datos.Append(FilaDeTexto(fila++, TitleStyleIndex, texto));
        void Seccion(string texto)
        {
            datos.Append(new Row { RowIndex = fila++ });
            datos.Append(FilaDeTexto(fila++, SectionStyleIndex, texto));
        }

        void Parrafo(string texto) => datos.Append(FilaDeTexto(fila++, BodyStyleIndex, texto));

        Titulo($"Plantilla de carga masiva — {BulkTramitesTemplateCatalog.DisplayName(tipo)} ({BulkTramitesTemplateCatalog.Version})");
        Parrafo(
            "Con este archivo creas hasta "
            + BulkTramitesTemplateCatalog.MaxRows.ToString(CultureInfo.InvariantCulture)
            + " trámites de una vez. Cada fila queda lista hasta el paso previo al cargue de "
            + "documentos: el vehículo consultado y los actores registrados. El cargue de "
            + "documentos y el resto del trámite se completan después, como en la creación individual.");

        Seccion("Cómo se usa");
        Parrafo("1. Abre la hoja «" + BulkTramitesTemplateCatalog.SheetName + "»: es la primera y es la única que se lee.");
        Parrafo("2. No toques la fila 1. Los encabezados son el contrato: si cambias uno, lo renombras, "
            + "lo mueves de sitio o agregas una columna, el archivo entero se rechaza.");
        Parrafo("3. Escribe una fila por trámite, desde la fila 2 y sin dejar filas vacías en medio.");
        Parrafo("4. Diligencia placa o VIN del vehículo; con uno de los dos alcanza para consultarlo.");
        Parrafo("5. Las columnas con lista desplegable se eligen, no se escriben. Al pararte en cualquier "
            + "celda aparece un recuadro con la explicación de esa columna.");
        Parrafo("6. Guarda el archivo como .xlsx y súbelo. El procesamiento ocurre en segundo plano: puedes "
            + "seguir usando la aplicación y consultar el resultado del lote desde /tramites.");

        Seccion("Reglas que conviene saber antes, y no después");
        Parrafo("• Si la consulta del vehículo falla, esa fila no crea el trámite y queda registrado el motivo.");
        Parrafo("• Si el vehículo se consulta bien pero falla la consulta de un actor, el trámite se crea "
            + "igual, marcado para retomarlo en el wizard desde el paso pendiente.");
        Parrafo("• Máximo " + BulkTramitesTemplateCatalog.MaxRows.ToString(CultureInfo.InvariantCulture)
            + " filas de datos. Con una más, el archivo completo se rechaza y no se procesa ninguna.");

        if (tipo != BulkTramitesTemplateType.Matricula)
        {
            Parrafo("• Con un solo actor por lado, el porcentaje se deja vacío. Con 2 o más, los "
                + "porcentajes son obligatorios, deben sumar 100 entre los del mismo lado y ninguno "
                + "puede quedar en 0.");
        }

        Seccion("Diccionario de columnas");
        var cabecera = new Row { RowIndex = fila };
        cabecera.Append(Cell($"A{fila}", "Columna", HeaderStyleIndex));
        cabecera.Append(Cell($"B{fila}", "Qué escribir", HeaderStyleIndex));
        datos.Append(cabecera);
        fila++;

        foreach (var columna in columnas)
        {
            var row = new Row { RowIndex = fila };
            row.Append(Cell($"A{fila}", columna.Header, BodyStyleIndex));
            row.Append(Cell($"B{fila}", columna.Guia, BodyStyleIndex));
            datos.Append(row);
            fila++;
        }

        return new Worksheet(columnasEstilo, datos);
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    // Hoja 3 — listas (oculta)
    // ════════════════════════════════════════════════════════════════════════════════════════════

    private sealed record CatalogRange(string Title, IReadOnlyList<string> Values, string Reference);

    private static List<CatalogRange> BuildCatalogRanges(IReadOnlyList<BulkTramitesColumnSpec> columnas)
    {
        var rangos = new List<CatalogRange>();

        foreach (var columna in columnas)
        {
            if (columna.Opciones is not { Count: > 0 } opciones
                || rangos.Any(r => Equal(r.Values, opciones)))
            {
                continue;
            }

            var letra = ColumnName(rangos.Count);
            rangos.Add(new CatalogRange(
                columna.Header,
                opciones,
                $"{BulkTramitesTemplateCatalog.ListsSheetName}!${letra}$2:${letra}${opciones.Count + 1}"));
        }

        return rangos;
    }

    private static Worksheet BuildListsSheet(List<CatalogRange> catalogos)
    {
        var datos = new SheetData();
        var maximo = catalogos.Count == 0 ? 0 : catalogos.Max(c => c.Values.Count);

        var cabecera = new Row { RowIndex = 1 };
        for (var i = 0; i < catalogos.Count; i++)
        {
            cabecera.Append(Cell(ColumnName(i) + "1", catalogos[i].Title, HeaderStyleIndex));
        }

        datos.Append(cabecera);

        for (var f = 0; f < maximo; f++)
        {
            var row = new Row { RowIndex = (uint)(f + 2) };
            for (var c = 0; c < catalogos.Count; c++)
            {
                if (f < catalogos[c].Values.Count)
                {
                    row.Append(Cell($"{ColumnName(c)}{f + 2}", catalogos[c].Values[f], TextStyleIndex));
                }
            }

            datos.Append(row);
        }

        return new Worksheet(datos);
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    // Estilos y utilidades
    // ════════════════════════════════════════════════════════════════════════════════════════════

    private static Stylesheet BuildStylesheet() => new(
        new Fonts(
            new Font(),
            new Font(new Bold()),
            new Font(new Bold(), new FontSize { Val = 14D }))
        { Count = 3 },
        new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FFE8EEFF" })
            {
                PatternType = PatternValues.Solid,
            }))
        { Count = 3 },
        new Borders(
            new Border(),
            new Border(new BottomBorder { Style = BorderStyleValues.Thin }))
        { Count = 2 },
        new CellStyleFormats(new CellFormat()) { Count = 1 },
        new CellFormats(
            new CellFormat(),
            new CellFormat { NumberFormatId = 49, ApplyNumberFormat = true },
            new CellFormat
            {
                NumberFormatId = 49,
                ApplyNumberFormat = true,
                FontId = 1,
                ApplyFont = true,
                FillId = 2,
                ApplyFill = true,
                BorderId = 1,
                ApplyBorder = true,
            },
            new CellFormat { FontId = 2, ApplyFont = true },
            new CellFormat { FontId = 1, ApplyFont = true },
            new CellFormat
            {
                ApplyAlignment = true,
                Alignment = new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top },
            })
        {
            Count = 6,
        });

    private static Cell Cell(string reference, string value, uint styleIndex) => new()
    {
        CellReference = reference,
        DataType = CellValues.InlineString,
        StyleIndex = styleIndex,
        InlineString = new InlineString(new Text(value)),
    };

    private static Row FilaDeTexto(uint index, uint styleIndex, string texto)
    {
        var row = new Row { RowIndex = index };
        row.Append(Cell($"A{index}", texto, styleIndex));
        return row;
    }

    private static bool Equal(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Count == b.Count && !a.Where((valor, i) => !string.Equals(valor, b[i], StringComparison.Ordinal)).Any();

    internal static string ColumnName(int index)
    {
        var nombre = string.Empty;
        var actual = index + 1;

        while (actual > 0)
        {
            var resto = (actual - 1) % 26;
            nombre = ((char)('A' + resto)).ToString(CultureInfo.InvariantCulture) + nombre;
            actual = (actual - 1) / 26;
        }

        return nombre;
    }
}
