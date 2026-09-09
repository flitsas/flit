using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Admin.Application.GeneracionDocumental.Batches;
using Flit.Admin.Application.GeneracionDocumental.Ports;

namespace Flit.Infrastructure.Documents.Standalone;

/// <summary>
/// Genera la plantilla XLSX <b>v1</b> que se descarga por <c>GET /lotes/plantilla</c> (CF-11,
/// Feature #12201 I3).
///
/// <para><b>Todas las columnas se emiten con formato TEXTO</b> (<c>numFmtId 49</c>, el <c>@</c> de
/// Excel). No es cosmética: es la mitigación que hace tratable el parser SAX (§8.4). Con formato
/// texto, Excel guarda <c>2026-01-31</c> como cadena y no como serial numérico, y una fecha que
/// igualmente llegue como número se rechaza —no se adivina— en
/// <see cref="StandaloneDocumentXlsxParser"/>.</para>
///
/// <para>Los encabezados salen de <see cref="StandaloneBatchTemplate.Headers"/>, la MISMA lista que
/// valida la carga: la plantilla que se entrega y la que se exige no pueden divergir.</para>
///
/// <para><b>EL ORDEN DE LAS HOJAS ES CONTRATO, NO ESTÉTICA.</b> El libro trae tres hojas —datos,
/// «Instrucciones» y «Listas» oculta— y la de datos va SIEMPRE primera, porque
/// <see cref="StandaloneDocumentXlsxParser"/> lee <c>Sheets.Elements&lt;Sheet&gt;().FirstOrDefault()</c>:
/// no busca por nombre. Poner la guía delante haría que la propia plantilla oficial se rechazara con
/// <c>template_invalid</c> en cuanto el usuario la subiera. Un test fija el orden.</para>
///
/// <para><b>Los desplegables no restringen de verdad, y por eso el servidor sigue validando.</b> La
/// validación de datos de Excel no se aplica a valores PEGADOS ni a archivos construidos por otra
/// herramienta: es una ayuda de captura, no un control. Su valor está en que el usuario no tenga que
/// adivinar el literal exacto —<c>DACION_EN_PAGO</c> no se acierta a la primera— y en que los
/// catálogos salen de las constantes del dominio, así que la lista ofrecida y la aceptada son el
/// mismo dato.</para>
/// </summary>
internal sealed class StandaloneDocumentXlsxTemplate : IStandaloneDocumentXlsxTemplate
{
    private const string Mimetype =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Índice del formato de celda «texto» dentro de la hoja de estilos que se construye abajo.</summary>
    private const uint TextStyleIndex = 1;

    private const uint HeaderStyleIndex = 2;
    private const uint TitleStyleIndex = 3;
    private const uint SectionStyleIndex = 4;
    private const uint BodyStyleIndex = 5;

    /// <summary>Título del emergente de ayuda. Excel corta a 32 caracteres, y varios encabezados
    /// del contrato pasan de ahí (<c>tiene_levantamiento_o_autorizacion</c> tiene 34), así que el
    /// título es fijo y el nombre de la columna ya se ve en la fila 1.</summary>
    private const string PromptTitle = "Cómo se llena";

    public RenderedStandaloneDocument Build()
    {
        using var buffer = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = BuildStylesheet();
            stylesPart.Stylesheet.Save();

            // Las listas se resuelven primero: la hoja de datos necesita saber en qué rango vive
            // cada catálogo para apuntar el desplegable.
            var catalogos = BuildCatalogRanges();

            var datosPart = workbookPart.AddNewPart<WorksheetPart>();
            datosPart.Worksheet = BuildDataSheet(catalogos);
            datosPart.Worksheet.Save();

            var guiaPart = workbookPart.AddNewPart<WorksheetPart>();
            guiaPart.Worksheet = BuildGuideSheet();
            guiaPart.Worksheet.Save();

            var listasPart = workbookPart.AddNewPart<WorksheetPart>();
            listasPart.Worksheet = BuildListsSheet(catalogos);
            listasPart.Worksheet.Save();

            workbookPart.Workbook.AppendChild(new Sheets(
                // PRIMERA, siempre. Ver la nota de la clase.
                new Sheet
                {
                    Id = workbookPart.GetIdOfPart(datosPart),
                    SheetId = 1,
                    Name = StandaloneBatchTemplate.SheetName,
                },
                new Sheet
                {
                    Id = workbookPart.GetIdOfPart(guiaPart),
                    SheetId = 2,
                    Name = StandaloneBatchTemplate.GuideSheetName,
                },
                new Sheet
                {
                    Id = workbookPart.GetIdOfPart(listasPart),
                    SheetId = 3,
                    Name = StandaloneBatchTemplate.ListsSheetName,
                    // Oculta: alimenta los desplegables y no es algo que el usuario deba tocar.
                    State = SheetStateValues.Hidden,
                }));

            workbookPart.Workbook.Save();
        }

        return new RenderedStandaloneDocument(
            $"plantilla-generacion-documental-{StandaloneBatchTemplate.Version}.xlsx",
            Mimetype,
            buffer.ToArray());
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    // Hoja 1 — datos
    // ════════════════════════════════════════════════════════════════════════════════════════════

    private static Worksheet BuildDataSheet(List<CatalogRange> catalogos)
    {
        // Encabezado congelado: con 52 columnas, perder de vista el nombre de la columna al bajar
        // por las filas es la vía directa a escribir el domicilio en la casilla del precio.
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

        var columnas = new Columns(new Column
        {
            Min = 1,
            Max = (uint)StandaloneBatchTemplate.Headers.Count,
            Width = 30,
            CustomWidth = true,
            Style = TextStyleIndex,
        });

        var header = new Row { RowIndex = 1 };
        for (var i = 0; i < StandaloneBatchTemplate.Headers.Count; i++)
        {
            header.Append(Cell(ColumnName(i) + "1", StandaloneBatchTemplate.Headers[i], HeaderStyleIndex));
        }

        return new Worksheet(vistas, columnas, new SheetData(header), BuildValidations(catalogos));
    }

    /// <summary>
    /// Una validación por columna sobre las 100 filas de datos. Las de catálogo cerrado son
    /// desplegables; las demás no restringen nada (<c>type="none"</c>) y existen solo para llevar el
    /// emergente de ayuda, que es la forma de que la guía viaje pegada a la celda y no en un
    /// documento aparte que nadie abre.
    /// </summary>
    private static DataValidations BuildValidations(List<CatalogRange> catalogos)
    {
        var validaciones = new DataValidations();

        for (var i = 0; i < StandaloneBatchTemplate.Columns.Count; i++)
        {
            var columna = StandaloneBatchTemplate.Columns[i];
            var letra = ColumnName(i);
            var rango = $"{letra}2:{letra}{StandaloneBatchTemplate.MaxRows + 1}";

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
                    + "con el detalle en el seguimiento del lote.";
            }
            else
            {
                validacion.Type = DataValidationValues.None;
            }

            validaciones.Append(validacion);
        }

        validaciones.Count = (uint)StandaloneBatchTemplate.Columns.Count;
        return validaciones;
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    // Hoja 2 — instrucciones
    // ════════════════════════════════════════════════════════════════════════════════════════════

    private static Worksheet BuildGuideSheet()
    {
        var columnas = new Columns(
            new Column { Min = 1, Max = 1, Width = 38, CustomWidth = true },
            new Column { Min = 2, Max = 2, Width = 26, CustomWidth = true },
            new Column { Min = 3, Max = 3, Width = 34, CustomWidth = true },
            new Column { Min = 4, Max = 4, Width = 95, CustomWidth = true });

        var datos = new SheetData();
        var fila = 1u;

        void Titulo(string texto) => datos.Append(FilaDeTexto(fila++, TitleStyleIndex, texto));
        void Seccion(string texto)
        {
            datos.Append(new Row { RowIndex = fila++ });
            datos.Append(FilaDeTexto(fila++, SectionStyleIndex, texto));
        }

        void Parrafo(string texto) => datos.Append(FilaDeTexto(fila++, BodyStyleIndex, texto));
        void Par(string izquierda, string derecha, string? nota = null)
        {
            var row = new Row { RowIndex = fila };
            row.Append(Cell($"A{fila}", izquierda, BodyStyleIndex));
            row.Append(Cell($"B{fila}", derecha, BodyStyleIndex));
            if (nota is not null)
            {
                row.Append(Cell($"C{fila}", nota, BodyStyleIndex));
            }

            datos.Append(row);
            fila++;
        }

        Titulo($"Plantilla de carga masiva — Generación documental ({StandaloneBatchTemplate.Version})");
        Parrafo(
            "Con este archivo emites hasta "
            + StandaloneBatchTemplate.MaxRows.ToString(CultureInfo.InvariantCulture)
            + " documentos de una vez: Certificados RUES, documentos de Transferencia de Dominio, o "
            + "una mezcla de los dos en el mismo lote. Escribe los datos en la hoja «"
            + StandaloneBatchTemplate.SheetName + "» y súbela desde la pestaña Carga masiva.");

        Seccion("Cómo se usa");
        Parrafo("1. Abre la hoja «" + StandaloneBatchTemplate.SheetName + "»: es la primera y es la única que se lee.");
        Parrafo("2. No toques la fila 1. Los encabezados son el contrato: si cambias uno, lo renombras, "
            + "lo mueves de sitio o agregas una columna, el archivo entero se rechaza con «template_invalid».");
        Parrafo("3. Escribe una fila por documento, desde la fila 2 y sin dejar filas vacías en medio.");
        Parrafo("4. Empieza siempre por la columna document_type: ella decide qué otras columnas tienes que llenar.");
        Parrafo("5. Las columnas con lista desplegable se eligen, no se escriben. Al pararte en cualquier "
            + "celda aparece un recuadro con la explicación de esa columna.");
        Parrafo("6. Guarda el archivo como .xlsx y súbelo. El procesamiento ocurre en segundo plano y "
            + "puedes seguir su avance fila por fila desde la propia aplicación.");

        Seccion("Reglas que conviene saber antes, y no después");
        Parrafo("• Una fila mala NO cancela el lote. Queda en error con su motivo y las demás se generan igual.");
        Parrafo("• Las fechas van como TEXTO en formato AAAA-MM-DD (2026-09-30). La plantilla ya trae las "
            + "celdas en formato Texto: si le cambias el formato y Excel la convierte en fecha, esa fila "
            + "se rechaza con «invalid_date». No se adivina el valor, porque adivinarlo produciría "
            + "documentos con la fecha equivocada.");
        Parrafo("• Para que una fila de transferencia genere, regimen_ninguna_aplica DEBE decir SI. Es la "
            + "declaración de que la operación no es ninguna de las once condiciones especiales de los "
            + "arts. 5.3.2.3 a 5.3.2.13. Dejarla vacía cuenta como «no respondida» y bloquea igual que NO.");
        Parrafo("• En el escenario B no hay adquirente ni precio: el acto es unilateral de la entidad "
            + "financiera. Las columnas adquirente_ y las de precio se dejan vacías, y el destinatario va "
            + "en las columnas locatario_.");
        Parrafo("• Los números de documento y de NIT van sin puntos, sin guiones y sin dígito de verificación.");
        Parrafo("• Una celda que no aplica se deja VACÍA. No escribas N/A ni guiones: se transcriben tal cual al documento.");
        Parrafo("• Máximo " + StandaloneBatchTemplate.MaxRows.ToString(CultureInfo.InvariantCulture)
            + " filas de datos. Con una más, el archivo completo se rechaza con «too_many_rows» y no se "
            + "procesa ninguna.");
        Parrafo("• Si vuelves a subir el mismo archivo por accidente, la aplicación te devuelve el lote que "
            + "ya existía en vez de duplicar los documentos.");

        Seccion("Ejemplo 1 — una fila de Certificado RUES");
        Parrafo("Solo dos columnas. Todo lo demás se deja vacío.");
        Par("Columna", "Valor", "Nota");
        Par(StandaloneBatchTemplate.DocumentType, "certificado_rues", "Elegido del desplegable.");
        Par(StandaloneBatchTemplate.Nit, "900123456", "Sin dígito de verificación: FLIT lo calcula.");

        Seccion("Ejemplo 2 — una transferencia del escenario A (traspaso ordinario)");
        Parrafo("Se muestran solo las columnas con contenido; las que no aparecen van vacías.");
        Par("Columna", "Valor", "Nota");
        Par(StandaloneBatchTemplate.DocumentType, "transferencia_dominio_generada", null);
        Par(StandaloneBatchTemplate.Escenario, "A", "Traspaso ordinario del art. 5.3.2.1.");
        Par(StandaloneBatchTemplate.Placa, "ABC123", "Seis caracteres, sin guion.");
        Par("marca", "CHEVROLET", "Como figura en la licencia de tránsito.");
        Par("linea", "SAIL LT", null);
        Par("modelo_anio", "2019", "Texto, no número.");
        Par("clase_vehiculo", "AUTOMOVIL", null);
        Par("color", "BLANCO", null);
        Par("no_motor", "A15SMS123456", null);
        Par("no_chasis", "LSGHD52H4KE123456", null);
        Par("servicio", "PARTICULAR", null);
        Par("organismo_transito", "SECRETARÍA DE TRÁNSITO DE ENVIGADO", null);
        Par("transferente_tipo_persona", "PJ", "Persona jurídica.");
        Par("transferente_nombre", "INVERSIONES DEMO S.A.S.", null);
        Par("transferente_tipo_doc", "NIT", null);
        Par("transferente_no_doc", "900123456", null);
        Par("transferente_domicilio", "MEDELLÍN", null);
        Par("transferente_representante_legal", "ANA MARÍA GÓMEZ", "Solo porque el transferente es PJ.");
        Par("transferente_cc_representante_legal", "43567890", null);
        Par("adquirente_tipo_persona", "PN", "Persona natural.");
        Par("adquirente_nombre", "CARLOS ANDRÉS RUIZ", null);
        Par("adquirente_tipo_doc", "CC", null);
        Par("adquirente_no_doc", "71234567", "Distinto al del transferente (VB-06).");
        Par("adquirente_domicilio", "ENVIGADO", null);
        Par("titulo_juridico", "COMPRAVENTA", "Es el único título que exige precio.");
        Par("precio_letras", "VEINTE MILLONES DE PESOS", null);
        Par("precio_numeros", "20000000", null);
        Par("forma_pago", "CONTADO", null);
        Par("asume_retencion_fuente", "SEGUN_LEY", null);
        Par("asume_derechos_tramite", "ADQUIRENTE", null);
        Par("asume_impuesto_vehiculo", "SEGUN_LEY", null);
        Par("ciudad_firma", "MEDELLÍN", null);
        Par(StandaloneBatchTemplate.FechaFirma, "2026-09-30", "Texto, formato AAAA-MM-DD.");
        Par("gravamen_activo", "NO", null);
        Par("tiene_levantamiento_o_autorizacion", "NO", "Solo importa si la anterior dice SI.");
        Par(StandaloneBatchTemplate.RegimenNingunaAplica, "SI", "Sin este SI la fila no se genera.");

        Seccion("Diccionario de columnas");
        Parrafo("Las " + StandaloneBatchTemplate.Columns.Count.ToString(CultureInfo.InvariantCulture)
            + " columnas de la hoja de datos, en el mismo orden en que aparecen.");

        var cabecera = new Row { RowIndex = fila };
        cabecera.Append(Cell($"A{fila}", "Columna", HeaderStyleIndex));
        cabecera.Append(Cell($"B{fila}", "Aplica a", HeaderStyleIndex));
        cabecera.Append(Cell($"C{fila}", "Valores admitidos", HeaderStyleIndex));
        cabecera.Append(Cell($"D{fila}", "Qué escribir", HeaderStyleIndex));
        datos.Append(cabecera);
        fila++;

        foreach (var columna in StandaloneBatchTemplate.Columns)
        {
            var row = new Row { RowIndex = fila };
            row.Append(Cell($"A{fila}", columna.Header, BodyStyleIndex));
            row.Append(Cell($"B{fila}", StandaloneBatchTemplate.ScopeLabel(columna.Aplica), BodyStyleIndex));
            row.Append(Cell(
                $"C{fila}",
                columna.Opciones is { Count: > 0 }
                    ? string.Join(" · ", columna.Opciones)
                    : columna.IsDate ? "Fecha AAAA-MM-DD (texto)" : "Texto libre",
                BodyStyleIndex));
            row.Append(Cell($"D{fila}", columna.Guia, BodyStyleIndex));
            datos.Append(row);
            fila++;
        }

        return new Worksheet(columnas, datos);
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    // Hoja 3 — listas (oculta)
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Un catálogo y el rango absoluto donde vive, listo para el <c>Formula1</c>.</summary>
    private sealed record CatalogRange(string Title, IReadOnlyList<string> Values, string Reference);

    /// <summary>
    /// Coloca cada catálogo DISTINTO en una columna de la hoja oculta y calcula su rango. Los
    /// catálogos repetidos —los tipos de documento aparecen en tres columnas, SI/NO en cuatro— se
    /// escriben una sola vez y las validaciones comparten el rango.
    /// </summary>
    private static List<CatalogRange> BuildCatalogRanges()
    {
        var rangos = new List<CatalogRange>();

        foreach (var columna in StandaloneBatchTemplate.Columns)
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
                $"{StandaloneBatchTemplate.ListsSheetName}!${letra}$2:${letra}${opciones.Count + 1}"));
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

    /// <summary>
    /// Hoja de estilos con lo justo. El formato 1 (<c>NumberFormatId = 49</c>, «Texto») es el que
    /// sostiene el contrato de §8.4; los demás son legibilidad.
    ///
    /// <para>El relleno en índice 1 es <c>Gray125</c> y no sobra: Excel espera que los dos primeros
    /// rellenos de todo libro sean <c>none</c> y <c>gray125</c>, y si no lo son marca el archivo
    /// como dañado y lo «repara» al abrirlo.</para>
    /// </summary>
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
            // 0 — general
            new CellFormat(),

            // 1 — texto: el formato que hace que Excel no convierta 2026-01-31 en un serial
            new CellFormat { NumberFormatId = 49, ApplyNumberFormat = true },

            // 2 — encabezado
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

            // 3 — título de la guía
            new CellFormat { FontId = 2, ApplyFont = true },

            // 4 — encabezado de sección de la guía
            new CellFormat { FontId = 1, ApplyFont = true },

            // 5 — cuerpo de la guía, con ajuste de texto para que las guías largas se lean enteras
            new CellFormat
            {
                ApplyAlignment = true,
                Alignment = new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top },
            })
        {
            Count = 6,
        });

    /// <summary>
    /// Celda de texto EMBEBIDO (<c>inlineStr</c>) en vez de tabla de cadenas compartidas: la
    /// plantilla se lee una vez y así el archivo no necesita un <c>sharedStrings.xml</c> paralelo.
    /// </summary>
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

    /// <summary>Índice 0-based a letras de columna (0 → <c>A</c>, 26 → <c>AA</c>).</summary>
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
