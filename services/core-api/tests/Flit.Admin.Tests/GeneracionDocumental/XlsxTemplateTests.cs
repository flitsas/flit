using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using Flit.Admin.Application.GeneracionDocumental.Batches;
using Flit.Infrastructure.Documents.Standalone;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// La plantilla XLSX que se entrega por <c>GET /lotes/plantilla</c> (CF-11, Feature #12201 I3).
///
/// <para><b>El hecho que sostiene a todos los demás es el primero:</b> la plantilla que FLIT
/// entrega tiene que poder subirse a FLIT. Suena obvio y no lo es —el parser lee la PRIMERA hoja
/// del libro, no la busca por nombre—, así que añadir la hoja de instrucciones delante de la de
/// datos haría que el archivo oficial se rechazara con <c>template_invalid</c> en cuanto el usuario
/// lo devolviera. Ese test es la red bajo esa trampa.</para>
///
/// <para>El resto verifica lo que hace útil a la plantilla: que cada columna lleve su explicación
/// pegada a la celda y que los desplegables ofrezcan EXACTAMENTE los valores que el servidor
/// acepta. Un desplegable con un valor de más no rompe nada ruidosamente: produce un documento al
/// que le falta una cláusula, sin un solo error a la vista.</para>
/// </summary>
public sealed class XlsxTemplateTests
{
    private readonly StandaloneDocumentXlsxTemplate _plantilla = new();

    // ── El hecho central: lo que se entrega, se puede volver a cargar ────────────────────────────

    [Fact]
    public void Plantilla_SeCargaConSuPropioParser_YNoTraeFilasDeDatos()
    {
        var archivo = _plantilla.Build();

        var resultado = new StandaloneDocumentXlsxParser().Parse(new MemoryStream(archivo.Content));

        // Encabezado aceptado (si no, sería template_invalid) y ni una fila de datos: la plantilla
        // llega vacía, para que el usuario no genere por accidente un documento de ejemplo.
        resultado.ErrorCode.Should().BeNull();
        resultado.Rows.Should().BeEmpty();
    }

    [Fact]
    public void Plantilla_DejaLaHojaDeDatosPrimera_ConLaGuiaDetrasYLasListasOcultas()
    {
        using var libro = Abrir(out _);

        var hojas = libro.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().ToList();

        hojas.Should().HaveCount(3);
        hojas[0].Name!.Value.Should().Be(StandaloneBatchTemplate.SheetName);
        hojas[1].Name!.Value.Should().Be(StandaloneBatchTemplate.GuideSheetName);
        hojas[2].Name!.Value.Should().Be(StandaloneBatchTemplate.ListsSheetName);

        // La hoja auxiliar no es parte de lo que el usuario llena.
        hojas[2].State!.Value.Should().Be(SheetStateValues.Hidden);
    }

    [Fact]
    public void Plantilla_EsUnLibroValidoSegunElEsquemaDeOpenXml()
    {
        // El XLSX se arma a mano, elemento por elemento, y el esquema es ESTRICTO en el orden: las
        // validaciones de datos van después de sheetData, las vistas antes de las columnas. Un orden
        // equivocado no falla al escribir — falla al ABRIR: Excel declara el archivo dañado, lo
        // «repara» y en la reparación se pierden justo los desplegables y la ayuda. Aquí no hay Excel
        // para descubrirlo, así que valida el esquema.
        using var libro = Abrir(out _);

        var errores = new OpenXmlValidator().Validate(libro).ToList();

        errores.Should().BeEmpty(
            "Excel repararía el archivo y descartaría lo reparado: {0}",
            string.Join(" | ", errores.Select(e => $"{e.Path?.XPath}: {e.Description}")));
    }

    // ── Ayuda pegada a la celda ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Plantilla_LlevaLaGuiaDeCadaColumnaComoMensajeDeLaCelda()
    {
        var validaciones = LeerValidaciones();

        foreach (var (columna, indice) in StandaloneBatchTemplate.Columns.Select((c, i) => (c, i)))
        {
            var validacion = validaciones[RangoDe(indice)];

            validacion.ShowInputMessage!.Value.Should().BeTrue(
                "la columna {0} debe explicarse al pararse en ella", columna.Header);
            validacion.Prompt!.Value.Should().Be(columna.Guia);
        }
    }

    [Fact]
    public void Guias_NoSuperanElTopeQueExcelTrunca()
    {
        // Excel no da error si el mensaje se pasa: lo CORTA. Una guía cortada a media frase es peor
        // que no tenerla, porque parece completa.
        foreach (var columna in StandaloneBatchTemplate.Columns)
        {
            columna.Guia.Length.Should().BeLessThanOrEqualTo(
                StandaloneBatchTemplate.MaxGuiaLength,
                "la guía de {0} se truncaría en Excel", columna.Header);
        }
    }

    // ── Desplegables: lo ofrecido y lo aceptado son el mismo dato ────────────────────────────────

    [Fact]
    public void Plantilla_ConvierteEnDesplegableCadaColumnaDeCatalogoCerrado()
    {
        var validaciones = LeerValidaciones();
        var listas = LeerHojaDeListas();

        var conCatalogo = 0;

        foreach (var (columna, indice) in StandaloneBatchTemplate.Columns.Select((c, i) => (c, i)))
        {
            var validacion = validaciones[RangoDe(indice)];

            if (columna.Opciones is not { Count: > 0 } esperadas)
            {
                validacion.Type!.Value.Should().Be(
                    DataValidationValues.None,
                    "{0} es texto libre y no debe restringirse", columna.Header);
                continue;
            }

            conCatalogo++;

            validacion.Type!.Value.Should().Be(DataValidationValues.List);

            // Lo que el desplegable ofrece de verdad: se sigue la referencia hasta la hoja oculta.
            var ofrecidos = listas[ColumnaDelRango(validacion.Formula1!.Text!)];
            ofrecidos.Should().Equal(
                esperadas,
                "el desplegable de {0} debe ofrecer exactamente lo que el backend acepta", columna.Header);
        }

        // Control: si un refactor dejara todas las columnas sin catálogo, los asertos de arriba
        // pasarían en vacío y el test no probaría nada.
        conCatalogo.Should().Be(16);
    }

    [Fact]
    public void Desplegables_CubrenLasCienFilasDeDatos()
    {
        var validaciones = LeerValidaciones();

        // Si el rango se quedara corto, las últimas filas perderían la ayuda y el desplegable justo
        // en el lote grande, que es donde más falta hacen.
        validaciones.Should().ContainKey($"A2:A{StandaloneBatchTemplate.MaxRows + 1}");
        validaciones.Should().HaveCount(StandaloneBatchTemplate.Columns.Count);
    }

    // ── Hoja de instrucciones ───────────────────────────────────────────────────────────────────

    [Fact]
    public void HojaDeInstrucciones_DocumentaTodasLasColumnas()
    {
        var textos = TextosDeLaColumnaA(StandaloneBatchTemplate.GuideSheetName);

        foreach (var columna in StandaloneBatchTemplate.Columns)
        {
            textos.Should().Contain(
                columna.Header,
                "el diccionario debe explicar {0}", columna.Header);
        }
    }

    [Fact]
    public void HojaDeInstrucciones_NoNombraColumnasQueNoExisten()
    {
        // Los ejemplos citan encabezados a mano. Un nombre mal escrito ahí enseña a llenar una
        // columna inexistente, y el usuario descubre el error cuando el lote ya falló.
        var conPintaDeEncabezado = new Regex("^[a-z][a-z0-9_]+$", RegexOptions.CultureInvariant);

        var citados = TextosDeLaColumnaA(StandaloneBatchTemplate.GuideSheetName)
            .Where(t => conPintaDeEncabezado.IsMatch(t))
            .Distinct()
            .ToList();

        citados.Should().NotBeEmpty();
        citados.Should().OnlyContain(t => StandaloneBatchTemplate.Headers.Contains(t));
    }

    // ── §8.4: el formato texto sigue en pie ─────────────────────────────────────────────────────

    [Fact]
    public void HojaDeDatos_ConservaElFormatoTextoEnTodasLasColumnas()
    {
        using var libro = Abrir(out var partes);

        var columnas = partes[StandaloneBatchTemplate.SheetName]
            .Worksheet.GetFirstChild<Columns>()!
            .Elements<Column>()
            .Single();

        columnas.Min!.Value.Should().Be(1);
        columnas.Max!.Value.Should().Be((uint)StandaloneBatchTemplate.Headers.Count);

        var formatos = libro.WorkbookPart!.WorkbookStylesPart!.Stylesheet.CellFormats!
            .Elements<CellFormat>()
            .ToList();

        // El estilo que la columna aplica a las celdas de datos tiene que ser el de TEXTO
        // (numFmtId 49): es lo que impide que Excel guarde 2026-01-31 como serial numérico.
        formatos[(int)columnas.Style!.Value].NumberFormatId!.Value.Should().Be(49U);
    }

    // ── Utilidades ──────────────────────────────────────────────────────────────────────────────

    private SpreadsheetDocument Abrir(out Dictionary<string, WorksheetPart> partes)
    {
        var archivo = _plantilla.Build();
        var libro = SpreadsheetDocument.Open(new MemoryStream(archivo.Content), isEditable: false);

        partes = libro.WorkbookPart!.Workbook.Sheets!
            .Elements<Sheet>()
            .ToDictionary(
                h => h.Name!.Value!,
                h => (WorksheetPart)libro.WorkbookPart!.GetPartById(h.Id!.Value!),
                StringComparer.Ordinal);

        return libro;
    }

    /// <summary>Validaciones de la hoja de datos, indexadas por el rango que cubren.</summary>
    private Dictionary<string, DataValidation> LeerValidaciones()
    {
        using var libro = Abrir(out var partes);

        return partes[StandaloneBatchTemplate.SheetName]
            .Worksheet.GetFirstChild<DataValidations>()!
            .Elements<DataValidation>()
            .ToDictionary(v => v.SequenceOfReferences!.InnerText!, v => (DataValidation)v.CloneNode(true), StringComparer.Ordinal);
    }

    /// <summary>Valores de cada columna de la hoja oculta, indexados por su letra.</summary>
    private Dictionary<string, List<string>> LeerHojaDeListas()
    {
        using var libro = Abrir(out var partes);

        var valores = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var fila in partes[StandaloneBatchTemplate.ListsSheetName]
                     .Worksheet.GetFirstChild<SheetData>()!
                     .Elements<Row>()
                     .Where(f => f.RowIndex!.Value > 1))
        {
            foreach (var celda in fila.Elements<Cell>())
            {
                var letra = SoloLetras(celda.CellReference!.Value!);
                if (!valores.TryGetValue(letra, out var lista))
                {
                    lista = [];
                    valores[letra] = lista;
                }

                lista.Add(celda.InlineString!.InnerText);
            }
        }

        return valores;
    }

    private List<string> TextosDeLaColumnaA(string hoja)
    {
        using var libro = Abrir(out var partes);

        return partes[hoja]
            .Worksheet.GetFirstChild<SheetData>()!
            .Elements<Row>()
            .SelectMany(f => f.Elements<Cell>())
            .Where(c => SoloLetras(c.CellReference!.Value!) == "A")
            .Select(c => c.InlineString?.InnerText ?? string.Empty)
            .ToList();
    }

    private static string RangoDe(int indice)
    {
        var letra = StandaloneDocumentXlsxTemplate.ColumnName(indice);
        return $"{letra}2:{letra}{StandaloneBatchTemplate.MaxRows + 1}";
    }

    /// <summary>De <c>Listas!$C$2:$C$4</c> saca <c>C</c>.</summary>
    private static string ColumnaDelRango(string referencia) =>
        SoloLetras(referencia.Split('!')[^1].TrimStart('$'));

    private static string SoloLetras(string referencia) =>
        new([.. referencia.TakeWhile(char.IsLetter)]);
}
