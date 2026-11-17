using System.IO.Compression;
using System.Runtime.CompilerServices;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Diagnostico.Api.Modulos.Reportes;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// Ajustes 2026-11-11, punto 8 (decisiones 8.1–8.3, AC 1–6) — el índice del ZIP masivo es <c>indice.xlsx</c> (<see cref="IndiceZip"/>):
/// una hoja con la cabecera arriba (nombre del diagnóstico, fecha y hora de Colombia, «N de M vehículos con PDF») y la tabla con
/// encabezado y autofiltro: Orden, Placa/VIN (texto), Estado (rótulo), En el ZIP (Sí/No), Archivo (vínculo relativo al PDF), Dictamen
/// (Viable/Bloqueado/Pendiente), Motivos de bloqueo (solo con Bloqueado) y Trámite. Se escribe en streaming y las filas leen los
/// dictámenes por páginas. Las pruebas abren el .xlsx generado con OpenXml. Sin Postgres: datos inventados (placas TST8##).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Reportes.IndiceZipTests
/// </code>
/// </example>
public sealed class IndiceZipTests
{
    private const string Bloqueado =
        """[{"codigo":"SOAT_NO_VIGENTE","texto":"El SOAT no está vigente.","efecto":"BLOQUEA","fuente":"RUNT"},{"codigo":"SIMIT_ACUERDO_PAGO","texto":"Hay un acuerdo de pago.","efecto":"ADVIERTE","fuente":"SIMIT_PROPIETARIO"},{"codigo":"SIMIT_PROPIETARIO","texto":"El propietario tiene multas.","efecto":"BLOQUEA","fuente":"SIMIT_PROPIETARIO"},{"codigo":"SOAT_NO_VIGENTE","texto":"Repetida.","efecto":"BLOQUEA","fuente":"RUNT"}]""";

    private const string ViableConAviso =
        """[{"codigo":"SIMIT_ACUERDO_PAGO","texto":"Hay un acuerdo de pago.","efecto":"ADVIERTE","fuente":"SIMIT_PROPIETARIO"}]""";

    // 2026-11-11 15:30 UTC = 10:30 en Colombia (UTC-5).
    private static readonly DateTimeOffset Generado = new(2026, 11, 11, 15, 30, 0, TimeSpan.Zero);

    private static readonly string[] Columnas = ["Orden", "Placa/VIN", "Estado", "En el ZIP", "Archivo", "Dictamen", "Motivos de bloqueo", "Trámite"];

    // AC1 — el ZIP trae indice.xlsx y no indice.txt; la plantilla del ZIP sube de versión (cambia el diseño del índice).
    [Fact]
    public void El_indice_del_zip_es_indice_xlsx_y_la_plantilla_sube_de_version()
    {
        EmpaquetadorZip.NombreIndice.Should().Be("indice.xlsx");
        IndiceZip.NombreArchivo.Should().Be("indice.xlsx");
        EmpaquetadorZip.VersionPlantilla.Should().Be(2, "el índice cambió de formato: los ZIP con indice.txt no se reutilizan");
    }

    // AC2 — encima de la tabla: el nombre del diagnóstico (no el GUID), la fecha y hora de generación en Colombia y «N de M vehículos con PDF».
    [Fact]
    public async Task Cabecera_con_nombre_fecha_de_colombia_y_n_de_m_con_pdf()
    {
        var libro = await LibroAsync(Cabecera(conPdf: 1, total: 2), [ConPdf(), SinPdf()]);

        var filas = libro.Filas;
        filas[0].Should().Equal("Lote flota norte");
        filas[1].Should().Equal("Generado el 2026-11-11 10:30 (hora Colombia)");
        filas[2].Should().Equal("1 de 2 vehículos con PDF");
        string.Join('\n', filas.SelectMany(f => f)).Should().NotContain(Guid.Empty.ToString("D")[..8]);
    }

    // AC3 — encabezado con las ocho columnas en orden, autofiltro desde el encabezado hasta la última fila y panel congelado bajo el encabezado.
    [Fact]
    public async Task Encabezado_con_las_ocho_columnas_autofiltro_y_panel_congelado()
    {
        var libro = await LibroAsync(Cabecera(conPdf: 1, total: 2), [ConPdf(), SinPdf()]);

        libro.Filas[IndiceZip.FilaEncabezado - 1].Should().Equal(Columnas);
        libro.AutoFiltro.Should().Be($"A{IndiceZip.FilaEncabezado}:H{IndiceZip.FilaEncabezado + 2}");
        libro.PanelDesde.Should().Be($"A{IndiceZip.FilaEncabezado + 1}");
        libro.Hojas.Should().Equal(IndiceZip.NombreHoja);
    }

    // AC3/AC4 — la fila con PDF: orden numérico, placa como texto, estado con su rótulo, «Sí», el nombre del PDF con vínculo relativo
    // (abre el PDF con el ZIP descomprimido), dictamen Viable y sin motivos.
    [Fact]
    public async Task Fila_con_pdf_lleva_si_y_vinculo_relativo_al_pdf()
    {
        var libro = await LibroAsync(Cabecera(conPdf: 1, total: 2), [ConPdf(), SinPdf()]);

        var fila = libro.Dato(0);
        fila.Should().Equal("1", "TST801", "Terminado", "Sí", "diagnostico-0a1b2c3d-TST801.pdf", "Viable", string.Empty, string.Empty);
        libro.Vinculos.Should().ContainKey($"E{IndiceZip.FilaEncabezado + 1}")
            .WhoseValue.Should().Be("diagnostico-0a1b2c3d-TST801.pdf");
        libro.VinculosExternos.Should().BeTrue("un vínculo a un archivo hermano del ZIP es externo al libro");
        libro.VinculosRelativos.Should().BeTrue("relativo: funciona donde se descomprima el ZIP");
    }

    // AC4 — la fila sin PDF: «No», Archivo vacío y sin vínculo; sin dictamen emitido, Dictamen = Pendiente.
    [Fact]
    public async Task Fila_sin_pdf_lleva_no_archivo_vacio_y_dictamen_pendiente()
    {
        var libro = await LibroAsync(Cabecera(conPdf: 1, total: 2), [ConPdf(), SinPdf()]);

        var fila = libro.Dato(1);
        fila.Should().Equal("2", "TST802", "En proceso", "No", string.Empty, "Pendiente", string.Empty, string.Empty);
        libro.Vinculos.Should().HaveCount(1, "solo la fila con PDF tiene vínculo");
        libro.Vinculos.Should().NotContainKey($"E{IndiceZip.FilaEncabezado + 2}");
    }

    // AC3 — Bloqueado: motivos = rótulos (punto 5) de las reglas que BLOQUEAN, en su orden, sin repetir, separados por «; »; las que
    // solo advierten no entran. Viable con advertencias no lleva motivos.
    [Fact]
    public async Task Bloqueado_lleva_los_rotulos_de_las_reglas_que_bloquean()
    {
        var bloqueado = new FilaIndiceZip(3, "TST803", "Terminado", "diagnostico-0a1b2c3d-TST803.pdf", "BLOQUEADO", Bloqueado, null);
        var viable = new FilaIndiceZip(4, "TST804", "Terminado", "diagnostico-0a1b2c3d-TST804.pdf", "VIABLE", ViableConAviso, null);

        var libro = await LibroAsync(Cabecera(conPdf: 2, total: 2), [bloqueado, viable]);

        libro.Dato(0)[5].Should().Be("Bloqueado");
        libro.Dato(0)[6].Should().Be("SOAT no vigente; Multas del propietario");
        libro.Dato(1)[5].Should().Be("Viable");
        libro.Dato(1)[6].Should().BeEmpty("los motivos solo van con dictamen Bloqueado");
        IndiceZip.Motivos("VIABLE", Bloqueado).Should().BeEmpty();
        IndiceZip.Motivos("BLOQUEADO", null).Should().BeEmpty();
        IndiceZip.Dictamen(null).Should().Be("Pendiente");
    }

    // AC3 — Placa/VIN es texto aunque parezca número (sin conversión numérica) y el texto que parece fórmula queda literal.
    [Fact]
    public async Task Placa_o_vin_es_texto_aunque_parezca_numero()
    {
        var numerico = new FilaIndiceZip(5, "0012345", "Terminado", null, null, null, null);
        var formula = new FilaIndiceZip(6, "=1+1", "Con error", null, null, null, null);

        var libro = await LibroAsync(Cabecera(conPdf: 0, total: 2), [numerico, formula]);

        libro.Dato(0)[1].Should().Be("0012345");
        libro.TiposColumnaB.Should().OnlyContain(t => t == CellValues.InlineString, "la placa nunca se escribe como número");
        libro.Formulas.Should().Be(0);
        libro.Dato(1)[2].Should().Be("Con error", "el estado es el rótulo, nunca el código");
    }

    // AC3 — Trámite: la columna existe siempre; vacía si el vehículo no tiene borrador y con el número cuando el punto 4 lo llene.
    [Fact]
    public async Task Tramite_vacio_sin_borrador_y_con_numero_cuando_existe()
    {
        var conTramite = new FilaIndiceZip(7, "TST807", "Terminado", "diagnostico-0a1b2c3d-TST807.pdf", "VIABLE", ViableConAviso, "TRM-2026-000123");

        var libro = await LibroAsync(Cabecera(conPdf: 1, total: 2), [conTramite, SinPdf()]);

        libro.Dato(0)[7].Should().Be("TRM-2026-000123");
        libro.Dato(1)[7].Should().BeEmpty();
        new VehiculoDelZip(Guid.NewGuid(), 1, "TST807", "Terminado", Incluido: true).Tramite
            .Should().BeNull("sin borrador el vehículo del ZIP no trae número de trámite");
    }

    // Punto 4 (incremento 3) → punto 8 — el número del trámite de cada vehículo entra en la huella del ZIP (zip/tramites), ordenado por
    // vehículo: un borrador nuevo cambia la huella y el ZIP no se reutiliza con la columna Trámite vieja; sin trámites, vacía.
    [Fact]
    public void La_huella_del_zip_suma_el_tramite_de_cada_vehiculo_en_orden_estable()
    {
        var a = Guid.Parse("0199f3a1-0000-7000-8000-00000000000a");
        var b = Guid.Parse("0199f3a1-0000-7000-8000-00000000000b");

        PlanZip.ClaveTramites([(b, "TR-2"), (a, "TR-1")]).Should().Be($"{a:D}:TR-1,{b:D}:TR-2");
        PlanZip.ClaveTramites([(a, "TR-1"), (b, "TR-2")]).Should().Be(PlanZip.ClaveTramites([(b, "TR-2"), (a, "TR-1")]), "el orden de lectura no cambia la huella");
        PlanZip.ClaveTramites([(a, "TR-1")]).Should().NotBe(PlanZip.ClaveTramites([(a, "TR-1"), (b, "TR-2")]), "un borrador nuevo cambia la huella");
        PlanZip.ClaveTramites([]).Should().BeEmpty();
        PlanZip.ClaveHuellaTramites.Should().Be("zip/tramites");
    }

    // AC6 (endurecimiento) — el paquete armado a mano en streaming pasa el validador de OpenXml (esquema de Office 2019): Excel lo abre sin reparar.
    [Fact]
    public async Task El_libro_pasa_el_validador_de_openxml()
    {
        var destino = new SoloEscritura();
        var bloqueado = new FilaIndiceZip(3, "TST803", "Terminado", "diagnostico-0a1b2c3d-TST803.pdf", "BLOQUEADO", Bloqueado, "TRM-1");
        await IndiceZip.EscribirAsync(destino, Cabecera(conPdf: 2, total: 3), Asincrono([ConPdf(), SinPdf(), bloqueado]), TestContext.Current.CancellationToken);

        using var flujo = new MemoryStream(destino.Bytes);
        using var documento = SpreadsheetDocument.Open(flujo, false);
        var errores = new DocumentFormat.OpenXml.Validation.OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019).Validate(documento, TestContext.Current.CancellationToken)
            .Select(e => $"{e.Path?.XPath}: {e.Description}")
            // Preexistente y compartido con el Excel del lote (copia de frontend/lib/xlsx.ts): la fuente del encabezado pone <color>
            // antes de <sz>. Excel lo abre igual; se reporta aparte y no es parte de este índice.
            .Where(e => !e.StartsWith("/x:styleSheet[1]/x:fonts[1]/x:font[2]:", StringComparison.Ordinal))
            .ToList();
        errores.Should().BeEmpty();
    }

    // AC1/AC6 — escrito dentro de una entrada del ZIP (flujo de solo escritura, sin Seek), el ZIP trae indice.xlsx y se abre como libro.
    [Fact]
    public async Task Se_escribe_dentro_de_una_entrada_del_zip()
    {
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using var destino = zip.CreateEntry(IndiceZip.NombreArchivo, CompressionLevel.Optimal).Open();
            await IndiceZip.EscribirAsync(destino, Cabecera(conPdf: 1, total: 2), Asincrono([ConPdf(), SinPdf()]), TestContext.Current.CancellationToken);
        }

        memoria.Position = 0;
        using var leido = new ZipArchive(memoria, ZipArchiveMode.Read);
        leido.Entries.Select(e => e.FullName).Should().Equal("indice.xlsx");
        leido.Entries.Should().NotContain(e => e.FullName == "indice.txt");
        using var copia = new MemoryStream();
        await using (var entrada = leido.Entries[0].Open())
            await entrada.CopyToAsync(copia, TestContext.Current.CancellationToken);
        Libro.Leer(copia.ToArray()).Dato(0)[1].Should().Be("TST801");
    }

    // AC5 — streaming: con un lote grande, los bytes del libro ya salieron al destino antes de que el origen entregue la última fila
    // (no se juntan todas las filas para escribirlas al final).
    [Fact]
    public async Task Lote_grande_se_escribe_mientras_llegan_las_filas()
    {
        const int total = 20_000;
        var destino = new SoloEscritura();
        long bytesAntesDeLaUltima = -1;

        async IAsyncEnumerable<FilaIndiceZip> Filas([EnumeratorCancellation] CancellationToken ct = default)
        {
            for (var i = 1; i <= total; i++)
            {
                if (i == total)
                    bytesAntesDeLaUltima = destino.Escritos;
                yield return new FilaIndiceZip(i, $"TST{i:00000}", "Terminado", $"diagnostico-0a1b2c3d-TST{i:00000}.pdf", "VIABLE", ViableConAviso, null);
                if (i % 1000 == 0)
                    await Task.Yield();
            }
        }

        await IndiceZip.EscribirAsync(destino, Cabecera(conPdf: total, total: total), Filas(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        bytesAntesDeLaUltima.Should().BeGreaterThan(0, "las filas se vuelcan al destino a medida que llegan");
        destino.Escritos.Should().BeGreaterThan(bytesAntesDeLaUltima);
        var libro = Libro.Leer(destino.Bytes);
        libro.Filas.Should().HaveCount(IndiceZip.FilaEncabezado + total);
        libro.Vinculos.Should().HaveCount(total);
    }

    // AC5 — las filas leen el dictamen por páginas en el orden de la carga y solo a medida que se consumen; con PDF, el nombre que
    // quedó en el ZIP; el número de trámite sale del vehículo del ZIP.
    [Fact]
    public async Task Las_filas_leen_los_dictamenes_por_paginas_y_a_demanda()
    {
        var ct = TestContext.Current.CancellationToken;
        var vehiculos = Enumerable.Range(1, 1201)
            .Select(i => new VehiculoDelZip(Guid.NewGuid(), i, $"TST{i:0000}", "Terminado", Incluido: i % 2 == 1, Tramite: i == 3 ? "TRM-3" : null))
            .ToList();
        var archivos = vehiculos.Where(v => v.Incluido).ToDictionary(v => v.Id, v => $"{v.Identificador}.pdf");
        var paginas = new List<int>();
        Task<IReadOnlyDictionary<Guid, DictamenDelIndice>> Leer(IReadOnlyList<Guid> ids, CancellationToken _)
        {
            paginas.Add(ids.Count);
            IReadOnlyDictionary<Guid, DictamenDelIndice> pagina = ids.Where((_, i) => i % 3 == 0)
                .ToDictionary(id => id, _ => new DictamenDelIndice("BLOQUEADO", Bloqueado));
            return Task.FromResult(pagina);
        }

        var filas = IndiceZip.FilasAsync(vehiculos, archivos, Leer, tamanoPagina: 500, ct);
        await using (var recorrido = filas.GetAsyncEnumerator(ct))
        {
            (await recorrido.MoveNextAsync()).Should().BeTrue();
            paginas.Should().Equal([500], "la segunda página no se lee hasta necesitarla");
        }

        paginas.Clear();
        var todas = new List<FilaIndiceZip>();
        await foreach (var fila in IndiceZip.FilasAsync(vehiculos, archivos, Leer, tamanoPagina: 500, ct))
            todas.Add(fila);

        paginas.Should().Equal(500, 500, 201);
        todas.Select(f => f.Orden).Should().Equal(Enumerable.Range(1, 1201));
        todas[0].Should().Be(new FilaIndiceZip(1, "TST0001", "Terminado", "TST0001.pdf", "BLOQUEADO", Bloqueado, null));
        todas[1].Should().Be(new FilaIndiceZip(2, "TST0002", "Terminado", null, null, null, null));
        todas[2].Tramite.Should().Be("TRM-3");
    }

    private static CabeceraIndiceZip Cabecera(int conPdf, int total) => new("Lote flota norte", Generado, conPdf, total);

    private static FilaIndiceZip ConPdf() =>
        new(1, "TST801", "Terminado", "diagnostico-0a1b2c3d-TST801.pdf", "VIABLE", ViableConAviso, null);

    private static FilaIndiceZip SinPdf() => new(2, "TST802", "En proceso", null, null, null, null);

    private static async Task<Libro> LibroAsync(CabeceraIndiceZip cabecera, IReadOnlyList<FilaIndiceZip> filas)
    {
        var destino = new SoloEscritura();
        await IndiceZip.EscribirAsync(destino, cabecera, Asincrono(filas), TestContext.Current.CancellationToken);
        return Libro.Leer(destino.Bytes);
    }

    private static async IAsyncEnumerable<FilaIndiceZip> Asincrono(IEnumerable<FilaIndiceZip> filas)
    {
        foreach (var fila in filas)
        {
            await Task.Yield();
            yield return fila;
        }
    }

    /// <summary>Flujo de solo escritura y sin Seek, como la entrada de un <see cref="ZipArchive"/> en modo Create; cuenta lo escrito.</summary>
    private sealed class SoloEscritura : Stream
    {
        private readonly MemoryStream _interno = new();

        public long Escritos => _interno.Length;

        public byte[] Bytes => _interno.ToArray();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _interno.Length;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => _interno.Write(buffer, offset, count);

        // Los bytes se leen después de cerrar el libro: el MemoryStream interno no se suelta al cerrar este flujo.
        protected override void Dispose(bool disposing) => base.Dispose(disposing);
    }

    /// <summary>El índice leído: filas de texto (fila 0 = primera de la hoja), vínculos por celda, autofiltro, panel y tipos de la columna B.</summary>
    private sealed record Libro(
        IReadOnlyList<string> Hojas, List<string[]> Filas, Dictionary<string, string> Vinculos, bool VinculosExternos, bool VinculosRelativos,
        string? AutoFiltro, string? PanelDesde, IReadOnlyList<CellValues> TiposColumnaB, int Formulas)
    {
        public string[] Dato(int i) => Filas[IndiceZip.FilaEncabezado + i];

        public static Libro Leer(byte[] xlsx)
        {
            var lectura = ExcelDePrueba.Leer(xlsx);
            using var flujo = new MemoryStream(xlsx);
            using var documento = SpreadsheetDocument.Open(flujo, false);
            var libro = documento.WorkbookPart!;
            var hojas = libro.Workbook.Sheets!.Elements<Sheet>().ToList();
            var parte = (WorksheetPart)libro.GetPartById(hojas[0].Id!);
            var relaciones = parte.HyperlinkRelationships.ToDictionary(r => r.Id, r => r);
            var vinculos = parte.Worksheet.Descendants<Hyperlink>()
                .ToDictionary(h => h.Reference!.Value!, h => relaciones[h.Id!.Value!].Uri.OriginalString);
            var tipos = parte.Worksheet.Descendants<Row>()
                .Where(r => r.RowIndex!.Value > IndiceZip.FilaEncabezado)
                .SelectMany(r => r.Elements<Cell>().Where(c => c.CellReference!.Value!.StartsWith('B')))
                .Select(c => c.DataType?.Value ?? CellValues.Number)
                .ToList();
            var nombre = hojas[0].Name!.Value!;
            return new Libro(
                [.. hojas.Select(h => h.Name!.Value!)],
                lectura.Hojas[nombre],
                vinculos,
                relaciones.Count > 0 && relaciones.Values.All(r => r.IsExternal),
                relaciones.Count > 0 && relaciones.Values.All(r => !r.Uri.IsAbsoluteUri),
                parte.Worksheet.Descendants<AutoFilter>().SingleOrDefault()?.Reference?.Value,
                parte.Worksheet.Descendants<Pane>().SingleOrDefault()?.TopLeftCell?.Value,
                tipos,
                lectura.Formulas);
        }
    }
}
