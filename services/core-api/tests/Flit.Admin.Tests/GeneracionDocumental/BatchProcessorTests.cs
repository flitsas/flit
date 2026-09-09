using System.Text.Json;
using Flit.Admin.Application.GeneracionDocumental.Batches;
using Flit.Admin.Application.GeneracionDocumental.GenerateRues;
using Flit.Admin.Application.GeneracionDocumental.GenerateTransferencia;
using Flit.Admin.Domain.GeneracionDocumental;
using Flit.Infrastructure.Documents.Standalone;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// Worker de lotes XLSX (HU #12210, CF-12/CF-13/CF-16/CF-24 en lote, R5).
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// var lote = await CargarAsync(FilaRues(), FilaTransferenciaValida());
/// await Runner().ProcessAsync(lote);
/// _batches.Completions[0].Status.Should().Be(StandaloneDocumentBatchStatus.Completed);
/// </code>
///
/// <para>El recorrido va de punta a punta con el parser REAL sobre el XLSX que quedó en storage: es
/// lo mismo que hace el worker en producción, sin base de datos ni hosting. Los handlers también son
/// los reales —el worker no puede tener una lógica de generación propia—; lo único simulado son las
/// fuentes externas (RUES, render y storage).</para>
/// </summary>
public sealed class BatchProcessorTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Usuario = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly FakeStandaloneDocumentBatchRepository _batches = new();
    private readonly FakeStandaloneDocumentRepository _documents = new();
    private readonly FakeStandaloneDocumentStorage _storage = new();
    private readonly FakeStandaloneRuesCompanyLookup _rues = new();
    private readonly FakeStandaloneRuesCertificateRenderer _renderer = new();
    private readonly FakeStandaloneTransferGenerator _generator = new();
    private readonly StandaloneDocumentXlsxParser _parser = new();

    private StandaloneDocumentBatchRunner Runner() => new(
        _batches,
        _documents,
        _storage,
        _parser,
        new GenerateRuesDocumentHandler(_documents, _rues, _renderer, _storage),
        new GenerateTransferenciaHandler(_documents, _generator, _storage));

    private async Task<StandaloneDocumentBatch> CargarAsync(params Dictionary<int, XlsxCell>[] filas)
    {
        var handler = new CreateBatchHandler(_batches, _parser, _storage);

        var result = await handler.HandleAsync(
            new CreateBatchCommand(
                Tenant, Usuario, "lote.xlsx", new MemoryStream(BatchXlsxBuilder.ConPlantillaV1(filas))),
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateBatchOutcome.Accepted);
        return _batches.Rows.Single(b => b.Id == result.BatchId);
    }

    // ── Filas de prueba (datos SINTÉTICOS) ─────────────────────────────────────────────────────

    private static Dictionary<int, XlsxCell> FilaRues(string nit = "900123456") =>
        BatchXlsxBuilder.Fila(
            XlsxCellKind.Shared,
            (StandaloneBatchTemplate.DocumentType, StandaloneDocumentType.CertificadoRues),
            (StandaloneBatchTemplate.Nit, nit));

    private static Dictionary<int, XlsxCell> FilaTransferencia(
        string placa = "ABC123",
        string regimenNinguna = "SI",
        string condiciones = "")
    {
        var valores = new List<(string, string)>
        {
            (StandaloneBatchTemplate.DocumentType, StandaloneDocumentType.TransferenciaDominioGenerada),
            (StandaloneBatchTemplate.Escenario, TransferScenario.TraspasoOrdinario),
            (StandaloneBatchTemplate.Placa, placa),
            ("marca", "MARCA DE PRUEBA"),
            ("linea", "LINEA DE PRUEBA"),
            ("modelo_anio", "2020"),
            ("clase_vehiculo", "AUTOMOVIL"),
            ("tipo_carroceria", "SEDAN"),
            ("color", "BLANCO"),
            ("no_motor", "MOT0000001"),
            ("no_chasis", "CHA0000001"),
            ("servicio", "PARTICULAR"),
            ("organismo_transito", "SECRETARIA DE MOVILIDAD DE PRUEBA"),
            ("transferente_tipo_persona", "PJ"),
            ("transferente_nombre", "EMPRESA TRANSFERENTE SAS"),
            ("transferente_tipo_doc", "NIT"),
            ("transferente_no_doc", "900123456"),
            ("transferente_domicilio", "CIUDAD DE PRUEBA"),
            ("transferente_representante_legal", "REPRESENTANTE DE PRUEBA"),
            ("transferente_cc_representante_legal", "10000001"),
            ("adquirente_tipo_persona", "PN"),
            ("adquirente_nombre", "PERSONA ADQUIRENTE DE PRUEBA"),
            ("adquirente_tipo_doc", "CC"),
            ("adquirente_no_doc", "10000002"),
            ("adquirente_domicilio", "OTRA CIUDAD DE PRUEBA"),
            ("titulo_juridico", TransferJuridicalTitle.Compraventa),
            ("precio_letras", "VEINTE MILLONES DE PESOS"),
            ("precio_numeros", "20.000.000"),
            ("forma_pago", "Transferencia electronica al momento de la entrega"),
            ("asume_retencion_fuente", "TRANSFERENTE"),
            ("asume_derechos_tramite", "COMPARTIDOS"),
            ("asume_impuesto_vehiculo", "ADQUIRENTE"),
            ("ciudad_firma", "CIUDAD DE PRUEBA"),
            (StandaloneBatchTemplate.FechaFirma, "2026-09-09"),
            (StandaloneBatchTemplate.RegimenNingunaAplica, regimenNinguna),
        };

        if (!string.IsNullOrEmpty(condiciones))
        {
            valores.Add((StandaloneBatchTemplate.RegimenCondiciones, condiciones));
        }

        return BatchXlsxBuilder.Fila(XlsxCellKind.Shared, [.. valores]);
    }

    private static IReadOnlyList<StandaloneDocumentValidationError> Errores(StandaloneDocument fila)
    {
        using var json = JsonDocument.Parse(fila.ValidationErrors);
        return [.. json.RootElement.EnumerateArray().Select(e => new StandaloneDocumentValidationError(
            e.GetProperty("code").GetString()!,
            e.GetProperty("field").GetString()!,
            e.GetProperty("message").GetString()!))];
    }

    // ── CF-12: tipos mezclados en un mismo lote ────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_ConTiposMezclados_UsaElHandlerDeCadaFilaYCierraEnCompleted()
    {
        var lote = await CargarAsync(FilaRues(), FilaTransferencia());

        await Runner().ProcessAsync(lote, TestContext.Current.CancellationToken);

        _rues.Calls.Should().Be(1, "solo la fila de RUES consulta al proveedor");
        _generator.Calls.Should().Be(1, "solo la fila de transferencia pasa por su generador");

        _documents.Rows.Should().HaveCount(2);
        _documents.Rows.Should().OnlyContain(r => r.Status == StandaloneDocumentStatus.Generated);
        _documents.Rows.Select(r => r.BatchId).Should().AllBeEquivalentTo(lote.Id);
        _documents.Rows.Select(r => r.RowNumber).Should().BeEquivalentTo(new int?[] { 1, 2 });

        _batches.Completions.Should().ContainSingle();
        _batches.Completions[0].Status.Should().Be(StandaloneDocumentBatchStatus.Completed);
        _batches.Completions[0].Generated.Should().Be(2);
        _batches.Completions[0].Errors.Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_ConTipoDeDocumentoDesconocido_DejaLaFilaEnErrorSinAbortarElLote()
    {
        var desconocida = BatchXlsxBuilder.Fila(
            XlsxCellKind.Shared,
            (StandaloneBatchTemplate.DocumentType, "certificado_inventado"),
            (StandaloneBatchTemplate.Nit, "900123456"));

        var lote = await CargarAsync(desconocida, FilaRues());

        await Runner().ProcessAsync(lote, TestContext.Current.CancellationToken);

        var fallida = _documents.Rows.Single(r => r.RowNumber == 1);
        fallida.Status.Should().Be(StandaloneDocumentStatus.Error);
        Errores(fallida).Should().ContainSingle()
            .Which.Code.Should().Be(StandaloneDocumentBatchRunner.ErrorUnknownDocumentType);

        _documents.Rows.Single(r => r.RowNumber == 2).Status
            .Should().Be(StandaloneDocumentStatus.Generated, "la fila siguiente sí se genera");

        _batches.Completions[0].Status.Should().Be(StandaloneDocumentBatchStatus.PartialFailure);
    }

    // ── CF-13: una fila inválida no cancela las demás ──────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_ConUnaPlacaMalFormada_DejaEsaFilaEnErrorYGeneraLasDemas()
    {
        Dictionary<int, XlsxCell>[] filas =
        [
            .. Enumerable.Range(0, 3).Select(_ => FilaTransferencia()),
            FilaTransferencia(placa: "**"),
            .. Enumerable.Range(0, 6).Select(_ => FilaTransferencia()),
        ];

        var lote = await CargarAsync(filas);

        await Runner().ProcessAsync(lote, TestContext.Current.CancellationToken);

        var cuarta = _documents.Rows.Single(r => r.RowNumber == 4);
        cuarta.Status.Should().Be(StandaloneDocumentStatus.Error);

        var errores = Errores(cuarta);
        errores.Should().ContainSingle(e => e.Code == "VB-02");

        var placa = errores.First(e => e.Code == "VB-02");
        placa.Field.Should().NotBeNullOrWhiteSpace();
        placa.Message.Should().NotContain("**", "el mensaje no refleja el valor capturado");

        _documents.Rows.Count(r => r.Status == StandaloneDocumentStatus.Generated).Should().Be(9);
        _batches.Completions[0].Status.Should().Be(StandaloneDocumentBatchStatus.PartialFailure);
        _batches.Completions[0].Generated.Should().Be(9);
        _batches.Completions[0].Errors.Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_ConSerialNumericoEnColumnaDeFecha_DejaLaFilaEnErrorConInvalidDate()
    {
        var fila = FilaTransferencia();
        fila[BatchXlsxBuilder.Col(StandaloneBatchTemplate.FechaFirma)] = new("46000", XlsxCellKind.Number);

        var lote = await CargarAsync(fila, FilaRues());

        await Runner().ProcessAsync(lote, TestContext.Current.CancellationToken);

        var fallida = _documents.Rows.Single(r => r.RowNumber == 1);
        fallida.Status.Should().Be(StandaloneDocumentStatus.Error);
        Errores(fallida).Should().ContainSingle()
            .Which.Code.Should().Be(StandaloneDocumentBatchRunner.ErrorInvalidDate);

        _generator.Calls.Should().Be(0, "una fecha ambigua no se convierte: la fila ni se intenta");
        _batches.Completions[0].Status.Should().Be(StandaloneDocumentBatchStatus.PartialFailure);
    }

    [Fact]
    public async Task ProcessAsync_ConTodasLasFilasEnError_CierraElLoteEnFailed()
    {
        var lote = await CargarAsync(FilaTransferencia(placa: "**"), FilaTransferencia(placa: "*"));

        await Runner().ProcessAsync(lote, TestContext.Current.CancellationToken);

        _batches.Completions[0].Status.Should().Be(StandaloneDocumentBatchStatus.Failed);
        _batches.Completions[0].Generated.Should().Be(0);
        _batches.Completions[0].Errors.Should().Be(2);
    }

    // ── CF-24 en lote: VB-07 con el artículo citado ────────────────────────────────────────────

    [Theory]
    [InlineData(TransferSpecialRegime.VehiculoBlindado, "art. 5.3.2.6")]
    [InlineData(TransferSpecialRegime.Sucesion, "art. 5.3.2.8")]
    [InlineData(TransferSpecialRegime.ComisoFiscalia, "art. 5.3.2.11")]
    public async Task ProcessAsync_ConFilaQueDeclaraRegimenEspecial_DejaVb07YSigueElLote(
        string condicion,
        string articulo)
    {
        var lote = await CargarAsync(
            FilaTransferencia(regimenNinguna: "NO", condiciones: condicion),
            FilaRues());

        await Runner().ProcessAsync(lote, TestContext.Current.CancellationToken);

        var bloqueada = _documents.Rows.Single(r => r.RowNumber == 1);
        bloqueada.Status.Should().Be(StandaloneDocumentStatus.Error);

        var vb07 = Errores(bloqueada).Should().ContainSingle(e => e.Code == "VB-07").Subject;
        vb07.Message.Should().Contain(articulo, "el bloqueo cita el artículo aplicable");

        _documents.Rows.Single(r => r.RowNumber == 2).Status
            .Should().Be(StandaloneDocumentStatus.Generated, "el resto del lote continúa");
    }

    [Fact]
    public async Task ProcessAsync_ConLasOnceCondiciones_BloqueaTodasConVb07()
    {
        // Una por una: el catálogo del dominio es la matriz de bloqueo y no se redeclara aquí.
        foreach (var condicion in TransferSpecialRegime.All)
        {
            var documentos = new FakeStandaloneDocumentRepository();
            var lotes = new FakeStandaloneDocumentBatchRepository();
            var storage = new FakeStandaloneDocumentStorage();

            var creador = new CreateBatchHandler(lotes, _parser, storage);
            var creado = await creador.HandleAsync(
                new CreateBatchCommand(
                    Tenant,
                    Usuario,
                    "lote.xlsx",
                    new MemoryStream(BatchXlsxBuilder.ConPlantillaV1(
                        FilaTransferencia(regimenNinguna: "NO", condiciones: condicion.Codigo)))),
                TestContext.Current.CancellationToken);

            var runner = new StandaloneDocumentBatchRunner(
                lotes,
                documentos,
                storage,
                _parser,
                new GenerateRuesDocumentHandler(documentos, new FakeStandaloneRuesCompanyLookup(), _renderer, storage),
                new GenerateTransferenciaHandler(documentos, _generator, storage));

            await runner.ProcessAsync(
                lotes.Rows.Single(b => b.Id == creado.BatchId), TestContext.Current.CancellationToken);

            var fila = documentos.Rows.Should().ContainSingle().Subject;
            fila.Status.Should().Be(StandaloneDocumentStatus.Error, condicion.Codigo);
            Errores(fila).Should().Contain(
                e => e.Code == "VB-07" && e.Message.Contains(condicion.Articulo, StringComparison.Ordinal),
                condicion.Codigo);
        }
    }

    // ── R5: reproceso tras el reaper ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_AlReprocesarUnLote_NoGeneraDosVecesLaMismaFila()
    {
        var lote = await CargarAsync(FilaRues(), FilaTransferencia());
        var runner = Runner();

        await runner.ProcessAsync(lote, TestContext.Current.CancellationToken);

        // El reaper devuelve el lote: el worker lo vuelve a recorrer entero.
        await runner.ProcessAsync(lote, TestContext.Current.CancellationToken);

        _documents.Rows.Should().HaveCount(2, "las filas ya materializadas se saltan");
        _rues.Calls.Should().Be(1, "no se gasta una segunda consulta al proveedor");
        _generator.Calls.Should().Be(1);
        _storage.Saved.Count(s => s.Tipo == "generacion_documental")
            .Should().Be(2, "un PDF por documento, ninguno repetido");

        _batches.Completions.Should().HaveCount(2);
        _batches.Completions[1].Generated.Should().Be(2, "el recuento se rehace desde las filas");
        _batches.Completions[1].Status.Should().Be(StandaloneDocumentBatchStatus.Completed);
    }

    [Fact]
    public async Task RunNextAsync_RecuperaUnLoteAtascadoYNoTocaLosRecientes()
    {
        var lote = await CargarAsync(FilaRues());

        // Simula el worker caído: el lote quedó en processing hace más del timeout.
        await _batches.ClaimNextAsync(
            DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        _batches.Rows.Single().Status.Should().Be(StandaloneDocumentBatchStatus.Processing);

        var reclamado = await Runner().RunNextAsync(
            TimeSpan.FromMinutes(15), TestContext.Current.CancellationToken);

        reclamado.Should().BeTrue("un claim vencido vuelve a ser reclamable");
        _batches.Completions.Should().ContainSingle();

        // Ya no queda nada por hacer: el lote está en estado terminal.
        var otra = await Runner().RunNextAsync(
            TimeSpan.FromMinutes(15), TestContext.Current.CancellationToken);
        otra.Should().BeFalse();
        _ = lote;
    }

    [Fact]
    public async Task ProcessAsync_SinArchivoFuenteEnStorage_CierraElLoteEnFailedYNoSeQuedaGirando()
    {
        var lote = await CargarAsync(FilaRues());
        _storage.Contenidos.Clear();

        await Runner().ProcessAsync(lote, TestContext.Current.CancellationToken);

        _batches.Completions[0].Status.Should().Be(StandaloneDocumentBatchStatus.Failed);
        _documents.Rows.Should().BeEmpty();
    }

    // ── PII: lo que el lote NO puede filtrar ───────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_EnUnaFilaFallida_NoGuardaDatosDeLaFilaEnInputSummary()
    {
        var lote = await CargarAsync(FilaTransferencia(placa: "**"));

        await Runner().ProcessAsync(lote, TestContext.Current.CancellationToken);

        var fallida = _documents.Rows.Single();
        fallida.InputSummary.Should().NotContain("PERSONA ADQUIRENTE DE PRUEBA");
        fallida.InputSummary.Should().NotContain("OTRA CIUDAD DE PRUEBA");
        fallida.InputSummary.Should().Contain("rowNumber");
    }
}
