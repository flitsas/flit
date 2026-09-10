using Flit.Admin.Application.GeneracionDocumental.Batches;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;
using Flit.Infrastructure.Documents.Standalone;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// Parser XLSX de la carga masiva (HU #12210, CF-11) sobre <c>DocumentFormat.OpenXml</c> crudo.
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// var parser = new StandaloneDocumentXlsxParser();
/// var result = parser.Parse(new MemoryStream(BatchXlsxBuilder.ConPlantillaV1(fila)));
/// result.ErrorCode.Should().BeNull();
/// </code>
///
/// <para>Los tres primeros hechos son las TRAMPAS del formato: celdas omitidas, shared vs inline y
/// el serial numérico de fecha. Si alguno se rompe, el lote no falla ruidosamente: emite documentos
/// con los datos corridos de columna o con la fecha equivocada.</para>
/// </summary>
public sealed class XlsxParserTests
{
    private readonly StandaloneDocumentXlsxParser _parser = new();

    // ── Trampa 1: las celdas vacías se OMITEN del XML ───────────────────────────────────────────

    [Fact]
    public void Parse_ConCeldasVaciasIntercaladas_ResuelveLaColumnaRealPorLaReferencia()
    {
        // Solo tres columnas escritas, muy separadas entre sí: entre ellas hay decenas de celdas que
        // Excel NO escribe. Contando posiciones, "OTRA CIUDAD" caería en la primera columna libre.
        var fila = BatchXlsxBuilder.Fila(
            XlsxCellKind.Shared,
            (StandaloneBatchTemplate.DocumentType, StandaloneDocumentType.CertificadoRues),
            (StandaloneBatchTemplate.Nit, "900123456"),
            ("ciudad_firma", "CIUDAD DE PRUEBA"));

        var result = _parser.Parse(new MemoryStream(BatchXlsxBuilder.ConPlantillaV1(fila)));

        result.ErrorCode.Should().BeNull();
        result.Rows.Should().ContainSingle();

        var valores = result.Rows[0].Values;
        valores[StandaloneBatchTemplate.DocumentType].Should().Be(StandaloneDocumentType.CertificadoRues);
        valores[StandaloneBatchTemplate.Nit].Should().Be("900123456");
        valores["ciudad_firma"].Should().Be("CIUDAD DE PRUEBA");

        // Lo que estaba vacío sigue vacío: nada se corrió de columna.
        valores[StandaloneBatchTemplate.Placa].Should().BeNull();
        valores["marca"].Should().BeNull();
        valores["adquirente_nombre"].Should().BeNull();
    }

    [Fact]
    public void Parse_ConHuecoAlPrincipioDeLaFila_NoDesplazaLasColumnasSiguientes()
    {
        // La primera columna (document_type) va VACÍA: la fila arranca en la segunda.
        var fila = new Dictionary<int, XlsxCell>
        {
            [BatchXlsxBuilder.Col(StandaloneBatchTemplate.Escenario)] = new("A", XlsxCellKind.Shared),
            [BatchXlsxBuilder.Col(StandaloneBatchTemplate.Nit)] = new("900123456", XlsxCellKind.Shared),
        };

        var result = _parser.Parse(new MemoryStream(BatchXlsxBuilder.ConPlantillaV1(fila)));

        result.Rows.Should().ContainSingle();
        result.Rows[0].Values[StandaloneBatchTemplate.DocumentType].Should().BeNull();
        result.Rows[0].Values[StandaloneBatchTemplate.Escenario].Should().Be("A");
        result.Rows[0].Values[StandaloneBatchTemplate.Nit].Should().Be("900123456");
    }

    // ── Trampa 2: t="s" (shared string) frente a t="inlineStr" ──────────────────────────────────

    [Fact]
    public void Parse_ResuelveCeldasSharedYCeldasInlineEnLaMismaFila()
    {
        var fila = new Dictionary<int, XlsxCell>
        {
            [BatchXlsxBuilder.Col(StandaloneBatchTemplate.DocumentType)] =
                new(StandaloneDocumentType.CertificadoRues, XlsxCellKind.Shared),
            [BatchXlsxBuilder.Col(StandaloneBatchTemplate.Nit)] =
                new("900123456", XlsxCellKind.Inline),
        };

        var result = _parser.Parse(new MemoryStream(BatchXlsxBuilder.ConPlantillaV1(fila)));

        result.ErrorCode.Should().BeNull();

        // Sin resolver la tabla de cadenas, la primera devolvería el ÍNDICE ("0") en vez del texto.
        result.Rows[0].Values[StandaloneBatchTemplate.DocumentType]
            .Should().Be(StandaloneDocumentType.CertificadoRues);
        result.Rows[0].Values[StandaloneBatchTemplate.Nit].Should().Be("900123456");
    }

    // ── Trampa 3: el serial numérico de fechas ─────────────────────────────────────────────────

    [Fact]
    public void Parse_ConCeldaNumericaEnColumnaDeFecha_RechazaLaFilaSinConvertirElSerial()
    {
        // 46 000 es un serial de fecha de Excel. Convertirlo a ciegas produciría un documento con
        // una fecha inventada; el contrato dice que la columna es texto.
        var fila = new Dictionary<int, XlsxCell>
        {
            [BatchXlsxBuilder.Col(StandaloneBatchTemplate.DocumentType)] =
                new(StandaloneDocumentType.TransferenciaDominioGenerada, XlsxCellKind.Shared),
            [BatchXlsxBuilder.Col(StandaloneBatchTemplate.Escenario)] = new("A", XlsxCellKind.Shared),
            [BatchXlsxBuilder.Col(StandaloneBatchTemplate.FechaFirma)] = new("46000", XlsxCellKind.Number),
        };

        var result = _parser.Parse(new MemoryStream(BatchXlsxBuilder.ConPlantillaV1(fila)));

        result.ErrorCode.Should().BeNull("el archivo entero es válido: lo que falla es UNA fila");
        result.Rows.Should().ContainSingle();

        var errores = result.Rows[0].Errors;
        errores.Should().ContainSingle();
        errores[0].Code.Should().Be(StandaloneDocumentBatchRunner.ErrorInvalidDate);
        errores[0].Field.Should().Be(StandaloneBatchTemplate.FechaFirma);
        errores[0].Message.Should().NotContain("46000", "el mensaje nunca refleja el valor capturado");

        result.Rows[0].Values[StandaloneBatchTemplate.FechaFirma].Should().BeNull();
    }

    [Fact]
    public void Parse_ConFechaEnTextoIso_NoMarcaError()
    {
        var fila = BatchXlsxBuilder.Fila(
            XlsxCellKind.Shared,
            (StandaloneBatchTemplate.DocumentType, StandaloneDocumentType.TransferenciaDominioGenerada),
            (StandaloneBatchTemplate.Escenario, "A"),
            (StandaloneBatchTemplate.FechaFirma, "2026-09-09"));

        var result = _parser.Parse(new MemoryStream(BatchXlsxBuilder.ConPlantillaV1(fila)));

        result.Rows[0].Errors.Should().BeEmpty();
        result.Rows[0].Values[StandaloneBatchTemplate.FechaFirma].Should().Be("2026-09-09");
    }

    [Fact]
    public void Parse_ConCeldaNumericaEnColumnaQueNoEsFecha_LaAceptaComoTexto()
    {
        // Un NIT tecleado como número es un descuido corriente y no ambiguo: se lee tal cual.
        var fila = new Dictionary<int, XlsxCell>
        {
            [BatchXlsxBuilder.Col(StandaloneBatchTemplate.DocumentType)] =
                new(StandaloneDocumentType.CertificadoRues, XlsxCellKind.Shared),
            [BatchXlsxBuilder.Col(StandaloneBatchTemplate.Nit)] = new("900123456", XlsxCellKind.Number),
        };

        var result = _parser.Parse(new MemoryStream(BatchXlsxBuilder.ConPlantillaV1(fila)));

        result.Rows[0].Errors.Should().BeEmpty();
        result.Rows[0].Values[StandaloneBatchTemplate.Nit].Should().Be("900123456");
    }

    // ── Rechazos del archivo completo (CF-11) ──────────────────────────────────────────────────

    [Fact]
    public void Parse_ConEncabezadoDistintoAlDeLaV1_RechazaConTemplateInvalid()
    {
        var header = StandaloneBatchTemplate.Headers.ToList();
        header[0] = "tipo_documento";

        var result = _parser.Parse(new MemoryStream(BatchXlsxBuilder.Build(header, [])));

        result.ErrorCode.Should().Be(StandaloneBatchParseError.TemplateInvalid);
        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ConColumnaDeMas_RechazaConTemplateInvalid()
    {
        var header = StandaloneBatchTemplate.Headers.ToList();
        header.Add("columna_extra");

        var result = _parser.Parse(new MemoryStream(BatchXlsxBuilder.Build(header, [])));

        result.ErrorCode.Should().Be(StandaloneBatchParseError.TemplateInvalid);
    }

    [Fact]
    public void Parse_Con100Filas_LasAcepta()
    {
        var filas = Enumerable.Range(0, 100)
            .Select(_ => BatchXlsxBuilder.Fila(
                XlsxCellKind.Shared,
                (StandaloneBatchTemplate.DocumentType, StandaloneDocumentType.CertificadoRues),
                (StandaloneBatchTemplate.Nit, "900123456")))
            .ToArray();

        var result = _parser.Parse(new MemoryStream(BatchXlsxBuilder.ConPlantillaV1(filas)));

        result.ErrorCode.Should().BeNull();
        result.Rows.Should().HaveCount(100);
        result.Rows[0].RowNumber.Should().Be(1, "1-based sin contar el encabezado");
        result.Rows[99].RowNumber.Should().Be(100);
    }

    [Fact]
    public void Parse_Con101Filas_RechazaConTooManyRows()
    {
        var filas = Enumerable.Range(0, 101)
            .Select(_ => BatchXlsxBuilder.Fila(
                XlsxCellKind.Shared,
                (StandaloneBatchTemplate.DocumentType, StandaloneDocumentType.CertificadoRues),
                (StandaloneBatchTemplate.Nit, "900123456")))
            .ToArray();

        var result = _parser.Parse(new MemoryStream(BatchXlsxBuilder.ConPlantillaV1(filas)));

        result.ErrorCode.Should().Be(StandaloneBatchParseError.TooManyRows);
        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ConArchivoQueNoEsXlsx_RechazaConInvalidFileYNoLanza()
    {
        // Un PDF con extensión cambiada: el MIME se verifica por CONTENIDO, no por el nombre.
        var pdf = new MemoryStream("%PDF-1.7 contenido que no es una hoja de cálculo"u8.ToArray());

        var result = _parser.Parse(pdf);

        result.ErrorCode.Should().Be(StandaloneBatchParseError.InvalidFile);
    }

    [Fact]
    public void Parse_ConZipQueNoEsXlsx_RechazaConInvalidFile()
    {
        // Firma ZIP correcta pero contenido corrupto: la apertura del paquete falla y no revienta.
        var zipRoto = new MemoryStream([0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x02, 0x03]);

        var result = _parser.Parse(zipRoto);

        result.ErrorCode.Should().Be(StandaloneBatchParseError.InvalidFile);
    }

    // ── Contrato con la plantilla que se entrega al usuario ────────────────────────────────────

    [Fact]
    public void Parse_DeLaPlantillaQueSeDescarga_ReconoceElEncabezado()
    {
        // El archivo que entrega GET /lotes/plantilla tiene que pasar la validación de POST /lotes.
        // Si divergen, el usuario recibe template_invalid con la plantilla oficial en la mano.
        var plantilla = new StandaloneDocumentXlsxTemplate().Build();

        var result = _parser.Parse(new MemoryStream(plantilla.Content));

        result.ErrorCode.Should().BeNull();
        result.Rows.Should().BeEmpty("la plantilla se entrega vacía");
        plantilla.Filename.Should().EndWith(".xlsx");
    }
}
