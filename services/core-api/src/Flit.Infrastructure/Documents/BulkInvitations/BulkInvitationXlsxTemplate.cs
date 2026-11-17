using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Modules.Security.Application.Auth.BulkInvitation;

namespace Flit.Infrastructure.Documents.BulkInvitations;

/// <summary>
/// Genera la plantilla XLSX de invitación masiva (HU #14422, Feature #14420, ADR-0077). Mismo contrato técnico que
/// <c>BulkTramitesXlsxTemplate</c>: todas las columnas en formato TEXTO, la hoja de datos siempre primera (el parser lee por
/// posición, no por nombre) y los desplegables como AYUDA de captura — el servidor vuelve a validar todo.
///
/// <para>El catálogo de la hoja «Roles» es EXACTAMENTE lo que entrega <see cref="IInvitationScopePolicy"/> para el contexto
/// (invitador + destino): nunca incluye SuperAdmin, roles de otro tipo de entidad ni, para un Gestor OT, roles de
/// administrador. Si el alcance no permite invitar, lanza <see cref="InvitationScopeDeniedException"/> y NO genera el archivo.</para>
///
/// <para>Todo texto que viene de la base (nombre y código del rol) pasa por <see cref="BulkInvitationFormulaGuard"/>: un valor que
/// empiece por <c>= + - @</c> no se interpreta como fórmula al abrir el archivo.</para>
/// </summary>
internal sealed class BulkInvitationXlsxTemplate(IInvitationScopePolicy scopePolicy, BulkInvitationOptions options)
    : IBulkInvitationXlsxTemplate
{
    private const string Mimetype =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const uint TextStyleIndex = 1;
    private const uint HeaderStyleIndex = 2;
    private const uint TitleStyleIndex = 3;
    private const uint SectionStyleIndex = 4;
    private const uint BodyStyleIndex = 5;

    private const string PromptTitle = "Cómo se llena";

    public async Task<RenderedBulkInvitationTemplate> BuildAsync(
        InvitationScopeContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var decision = await scopePolicy.ResolveAsync(context, cancellationToken).ConfigureAwait(false);
        if (!decision.Allowed)
        {
            throw new InvitationScopeDeniedException(decision.DenyReason ?? InvitationScopeReasons.InviterNotAuthorized);
        }

        var roles = decision.AssignableRoles.ToList();

        using var buffer = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = BuildStylesheet();
            stylesPart.Stylesheet.Save();

            var dataPart = workbookPart.AddNewPart<WorksheetPart>();
            dataPart.Worksheet = BuildDataSheet(roles.Count);
            dataPart.Worksheet.Save();

            var guidePart = workbookPart.AddNewPart<WorksheetPart>();
            guidePart.Worksheet = BuildGuideSheet();
            guidePart.Worksheet.Save();

            var rolesPart = workbookPart.AddNewPart<WorksheetPart>();
            rolesPart.Worksheet = BuildRolesSheet(roles);
            rolesPart.Worksheet.Save();

            workbookPart.Workbook.AppendChild(new Sheets(
                new Sheet
                {
                    Id = workbookPart.GetIdOfPart(dataPart),
                    SheetId = 1,
                    Name = BulkInvitationTemplateCatalog.SheetName,
                },
                new Sheet
                {
                    Id = workbookPart.GetIdOfPart(guidePart),
                    SheetId = 2,
                    Name = BulkInvitationTemplateCatalog.GuideSheetName,
                },
                new Sheet
                {
                    Id = workbookPart.GetIdOfPart(rolesPart),
                    SheetId = 3,
                    Name = BulkInvitationTemplateCatalog.ListsSheetName,
                }));

            workbookPart.Workbook.Save();
        }

        return new RenderedBulkInvitationTemplate(
            $"plantilla-invitacion-masiva-{BulkInvitationTemplateCatalog.Version}.xlsx",
            Mimetype,
            buffer.ToArray());
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    // Hoja 1 — datos
    // ════════════════════════════════════════════════════════════════════════════════════════════

    private Worksheet BuildDataSheet(int roleCount)
    {
        var headers = BulkInvitationTemplateCatalog.Headers;

        var views = new SheetViews(new SheetView
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

        var columns = new Columns(new Column
        {
            Min = 1,
            Max = (uint)headers.Count,
            Width = 34,
            CustomWidth = true,
            Style = TextStyleIndex,
        });

        var header = new Row { RowIndex = 1 };
        for (var i = 0; i < headers.Count; i++)
        {
            header.Append(Cell(ColumnName(i) + "1", headers[i], HeaderStyleIndex));
        }

        var lastRow = options.MaxRows + 1;
        var validations = new DataValidations();

        validations.Append(PromptValidation(
            $"A2:A{lastRow.ToString(CultureInfo.InvariantCulture)}",
            "Nombre y apellido de la persona (hasta 200 caracteres)."));
        validations.Append(PromptValidation(
            $"B2:B{lastRow.ToString(CultureInfo.InvariantCulture)}",
            "Correo de la persona. Cada correo puede aparecer una sola vez en el archivo."));

        var roleValidation = PromptValidation(
            $"C2:C{lastRow.ToString(CultureInfo.InvariantCulture)}",
            "Elige el rol de la lista. Es un solo rol por persona.");
        if (roleCount > 0)
        {
            roleValidation.Type = DataValidationValues.List;
            roleValidation.Formula1 = new Formula1(
                $"{BulkInvitationTemplateCatalog.ListsSheetName}!$A$2:$A${(roleCount + 1).ToString(CultureInfo.InvariantCulture)}");
            roleValidation.ShowErrorMessage = true;
            roleValidation.ErrorStyle = DataValidationErrorStyleValues.Stop;
            roleValidation.ErrorTitle = "Valor no admitido";
            roleValidation.Error =
                "Elige uno de los roles de la lista. Si escribes otro, la fila quedará rechazada en el reporte.";
        }

        validations.Append(roleValidation);
        validations.Count = (uint)validations.ChildElements.Count;

        return new Worksheet(views, columns, new SheetData(header), validations);
    }

    private static DataValidation PromptValidation(string range, string prompt) => new()
    {
        SequenceOfReferences = new ListValue<StringValue> { InnerText = range },
        Type = DataValidationValues.None,
        AllowBlank = true,
        ShowInputMessage = true,
        PromptTitle = PromptTitle,
        // Prompt, no una guía larga: Excel descarta las validaciones si el emergente pasa de 255 caracteres.
        Prompt = prompt,
    };

    // ════════════════════════════════════════════════════════════════════════════════════════════
    // Hoja 2 — instrucciones
    // ════════════════════════════════════════════════════════════════════════════════════════════

    private Worksheet BuildGuideSheet()
    {
        var columns = new Columns(
            new Column { Min = 1, Max = 1, Width = 110, CustomWidth = true });

        var data = new SheetData();
        var row = 1u;

        void Title(string text) => data.Append(TextRow(row++, TitleStyleIndex, text));

        void Section(string text)
        {
            data.Append(new Row { RowIndex = row++ });
            data.Append(TextRow(row++, SectionStyleIndex, text));
        }

        void Paragraph(string text) => data.Append(TextRow(row++, BodyStyleIndex, text));

        var maxRows = options.MaxRows.ToString(CultureInfo.InvariantCulture);

        Title($"Plantilla de invitación masiva de usuarios ({BulkInvitationTemplateCatalog.Version})");
        Paragraph(
            $"Con este archivo invitas hasta {maxRows} personas de una vez. A cada una le llega el correo de activación "
            + "de su cuenta, igual que cuando invitas de a una.");

        Section("Cómo se usa");
        Paragraph($"1. Abre la hoja «{BulkInvitationTemplateCatalog.SheetName}»: es la primera y es la única que se lee.");
        Paragraph("2. No toques la fila 1. Los encabezados son el contrato: si cambias uno, lo mueves de sitio o falta una "
            + "columna, el archivo entero se rechaza.");
        Paragraph("3. Escribe una persona por fila, desde la fila 2 y sin dejar filas vacías en medio.");
        Paragraph($"4. En «{BulkInvitationTemplateCatalog.RoleHeader}» elige el rol de la lista desplegable (hoja «{BulkInvitationTemplateCatalog.ListsSheetName}»). Es UN rol por persona.");
        Paragraph("5. Guarda el archivo como .xlsx y súbelo. Primero se te muestra una revisión fila por fila; "
            + "las invitaciones solo se crean cuando confirmas.");

        Section("Reglas que conviene saber antes, y no después");
        Paragraph($"• Máximo {maxRows} filas de datos. Con una más, el archivo completo se rechaza y no se procesa ninguna.");
        Paragraph("• Una fila con error no tumba el lote: las demás se invitan y el reporte te dice qué corregir.");
        Paragraph("• Un correo que ya tiene cuenta o una invitación pendiente se rechaza con el mismo mensaje en ambos casos.");
        Paragraph("• Si repites el archivo ya confirmado, las filas ya invitadas se rechazan; no se duplica nada.");
        Paragraph("• Las celdas que empiezan por =, +, - o @ se tratan como texto y la fila se rechaza (no se admiten fórmulas).");

        Section("Columnas");
        Paragraph($"{BulkInvitationTemplateCatalog.FullNameHeader}: nombre y apellido, hasta 200 caracteres.");
        Paragraph($"{BulkInvitationTemplateCatalog.EmailHeader}: correo de la persona, único dentro del archivo.");
        Paragraph($"{BulkInvitationTemplateCatalog.RoleHeader}: nombre del rol, tal como aparece en la hoja «{BulkInvitationTemplateCatalog.ListsSheetName}».");

        return new Worksheet(columns, data);
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    // Hoja 3 — catálogo de roles (lo que entrega IInvitationScopePolicy)
    // ════════════════════════════════════════════════════════════════════════════════════════════

    private static Worksheet BuildRolesSheet(List<InvitationRoleOption> roles)
    {
        var columns = new Columns(
            new Column { Min = 1, Max = 2, Width = 38, CustomWidth = true, Style = TextStyleIndex });

        var data = new SheetData();

        var header = new Row { RowIndex = 1 };
        header.Append(Cell("A1", "Rol", HeaderStyleIndex));
        header.Append(Cell("B1", "Código", HeaderStyleIndex));
        data.Append(header);

        for (var i = 0; i < roles.Count; i++)
        {
            var index = (uint)(i + 2);
            var row = new Row { RowIndex = index };
            row.Append(Cell($"A{index}", roles[i].Name, TextStyleIndex));
            row.Append(Cell($"B{index}", roles[i].Code, TextStyleIndex));
            data.Append(row);
        }

        return new Worksheet(columns, data);
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

    /// <summary>Celda de texto en línea. Todo valor pasa por el guard de fórmulas: nada del catálogo se interpreta como fórmula.</summary>
    private static Cell Cell(string reference, string value, uint styleIndex) => new()
    {
        CellReference = reference,
        DataType = CellValues.InlineString,
        StyleIndex = styleIndex,
        InlineString = new InlineString(new Text(BulkInvitationFormulaGuard.Neutralize(value) ?? string.Empty)),
    };

    private static Row TextRow(uint index, uint styleIndex, string text)
    {
        var row = new Row { RowIndex = index };
        row.Append(Cell($"A{index}", text, styleIndex));
        return row;
    }

    internal static string ColumnName(int index)
    {
        var name = string.Empty;
        var current = index + 1;

        while (current > 0)
        {
            var remainder = (current - 1) % 26;
            name = ((char)('A' + remainder)).ToString(CultureInfo.InvariantCulture) + name;
            current = (current - 1) / 26;
        }

        return name;
    }
}
