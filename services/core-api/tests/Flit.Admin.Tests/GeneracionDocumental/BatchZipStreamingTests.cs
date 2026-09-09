using System.IO.Compression;
using Flit.Admin.Application.GeneracionDocumental.Batches;
using Flit.Admin.Domain.GeneracionDocumental;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// Descarga ZIP del lote por streaming (CF-15, HU #12211).
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// var plan = await handler.PrepareAsync(tenantId, batchId, ct);
/// if (plan.Outcome == StandaloneBatchZipOutcome.Ok)
/// {
///     await handler.WriteAsync(plan.Entries, cuerpoDeLaRespuesta, ct);
/// }
/// </code>
///
/// <para>Los tres AC que aquí se demuestran son propiedades observables, no intenciones: el ZIP se
/// ABRE y se cuentan sus entradas; el storage lleva la cuenta de binarios abiertos a la vez; y el
/// doble del storage registra cada <c>SaveAsync</c>, así que «no se persiste» se comprueba mirando
/// que no hubo ninguno.</para>
/// </summary>
public sealed class BatchZipStreamingTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtroTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Usuario = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly FakeStandaloneDocumentBatchRepository _batches = new();
    private readonly FakeStandaloneDocumentRepository _documents = new();
    private readonly FakeStandaloneDocumentStorage _storage = new();

    private DownloadBatchZipHandler Handler() => new(_batches, _documents, _storage);

    // ── CF-15: solo los generados, y el ZIP no se persiste ─────────────────────────────────────

    [Fact]
    public async Task Zip_DeLoteCon9GeneradasY1Error_Trae9EntradasYExcluyeLaFallida()
    {
        var lote = Sembrar(generadas: 9, errores: 1);

        var plan = await Handler().PrepareAsync(Tenant, lote, TestContext.Current.CancellationToken);

        plan.Outcome.Should().Be(StandaloneBatchZipOutcome.Ok);
        plan.Entries.Should().HaveCount(9, "la fila en error no tiene binario que empaquetar");

        using var destino = new MemoryStream();
        var escritas = await Handler().WriteAsync(
            plan.Entries, destino, TestContext.Current.CancellationToken);

        escritas.Should().Be(9);

        destino.Position = 0;
        using var zip = new ZipArchive(destino, ZipArchiveMode.Read);

        zip.Entries.Should().HaveCount(9);
        zip.Entries.Select(e => e.FullName).Should()
            .NotContain(n => n.Contains("fila-10", StringComparison.Ordinal),
                "la fila 10 quedo en error y no puede aparecer en el ZIP");
    }

    [Fact]
    public async Task Zip_NoSePersisteNiEnStorageNiEnBaseDeDatos()
    {
        var lote = Sembrar(generadas: 3, errores: 0);
        var filasAntes = _documents.Rows.Count;

        var plan = await Handler().PrepareAsync(Tenant, lote, TestContext.Current.CancellationToken);

        using var destino = new MemoryStream();
        await Handler().WriteAsync(plan.Entries, destino, TestContext.Current.CancellationToken);

        _storage.Saved.Should().BeEmpty("el ZIP se arma contra la respuesta, no se guarda");
        _documents.Rows.Should().HaveCount(filasAntes, "descargar un ZIP no crea filas");
        _batches.Completions.Should().BeEmpty("descargar no cierra ni modifica el lote");
    }

    [Fact]
    public async Task Zip_SostieneUnPdfALaVezEnMemoria()
    {
        var lote = Sembrar(generadas: 10, errores: 0);

        var plan = await Handler().PrepareAsync(Tenant, lote, TestContext.Current.CancellationToken);

        using var destino = new MemoryStream();
        await Handler().WriteAsync(plan.Entries, destino, TestContext.Current.CancellationToken);

        _storage.Abiertos.Should().HaveCount(10, "se leyo cada binario exactamente una vez");
        _storage.MaximoAbiertosSimultaneos.Should().Be(
            1, "la cota de memoria del ZIP es un PDF a la vez, no el lote entero");
        _storage.AbiertosAhora.Should().Be(0, "todos los binarios quedaron cerrados");
    }

    [Fact]
    public async Task Zip_ConservaElContenidoDeCadaDocumento()
    {
        var lote = Sembrar(generadas: 2, errores: 0);

        var plan = await Handler().PrepareAsync(Tenant, lote, TestContext.Current.CancellationToken);

        using var destino = new MemoryStream();
        await Handler().WriteAsync(plan.Entries, destino, TestContext.Current.CancellationToken);

        destino.Position = 0;
        using var zip = new ZipArchive(destino, ZipArchiveMode.Read);

        var primera = zip.Entries.OrderBy(e => e.FullName).First();
        using var lector = new StreamReader(primera.Open());
        var contenido = await lector.ReadToEndAsync(TestContext.Current.CancellationToken);

        contenido.Should().StartWith("%PDF", "el binario viaja intacto dentro del ZIP");
    }

    [Fact]
    public async Task Zip_NombraLasEntradasConElNumeroDeFilaParaNoColisionar()
    {
        // Dos filas del mismo lote con el MISMO nombre de archivo: sin prefijo, el ZIP perderia una.
        var lote = Guid.CreateVersion7();
        InsertarLote(lote, total: 2);
        InsertarGenerada(lote, fila: 1, filename: "certificado_rues.pdf");
        InsertarGenerada(lote, fila: 2, filename: "certificado_rues.pdf");

        var plan = await Handler().PrepareAsync(Tenant, lote, TestContext.Current.CancellationToken);

        using var destino = new MemoryStream();
        await Handler().WriteAsync(plan.Entries, destino, TestContext.Current.CancellationToken);

        destino.Position = 0;
        using var zip = new ZipArchive(destino, ZipArchiveMode.Read);

        zip.Entries.Should().HaveCount(2);
        zip.Entries.Select(e => e.FullName).Should()
            .BeEquivalentTo(["001-certificado_rues.pdf", "002-certificado_rues.pdf"]);
    }

    // ── Lote sin documentos generados ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Zip_DeLoteSinNingunGenerado_DevuelveNoDocumentsYNoEscribeArchivo()
    {
        var lote = Sembrar(generadas: 0, errores: 4);

        var plan = await Handler().PrepareAsync(Tenant, lote, TestContext.Current.CancellationToken);

        plan.Outcome.Should().Be(
            StandaloneBatchZipOutcome.NoDocuments,
            "un lote entero en error se explica, no se entrega como ZIP vacio");
        plan.Entries.Should().BeEmpty();
    }

    // ── Aislamiento por AUTORIZACION (no por RLS) ──────────────────────────────────────────────

    [Fact]
    public async Task Zip_DeLoteDeOtroTenant_DevuelveNotFoundSinAbrirNingunBinario()
    {
        var lote = Sembrar(generadas: 5, errores: 0);

        var plan = await Handler().PrepareAsync(OtroTenant, lote, TestContext.Current.CancellationToken);

        plan.Outcome.Should().Be(
            StandaloneBatchZipOutcome.NotFound,
            "404 y no 403: un 403 confirmaria que el lote existe");
        plan.Entries.Should().BeEmpty();
        _storage.Abiertos.Should().BeEmpty("no se toco un solo binario ajeno");
        _storage.Presigned.Should().BeEmpty("no se firmo ninguna URL de otro tenant");
    }

    [Fact]
    public async Task Zip_ConBinarioDesaparecidoDeStorage_SaltaLaEntradaSinAbortarLaDescarga()
    {
        var lote = Sembrar(generadas: 3, errores: 0);

        // Se borra el binario de una fila: en storage puede faltar lo que la BD dice que existe.
        var huerfana = _documents.Rows.First(r => r.BatchId == lote && r.StoragePath is not null);
        _storage.Contenidos.Remove(huerfana.StoragePath!);

        var plan = await Handler().PrepareAsync(Tenant, lote, TestContext.Current.CancellationToken);

        using var destino = new MemoryStream();
        var escritas = await Handler().WriteAsync(
            plan.Entries, destino, TestContext.Current.CancellationToken);

        escritas.Should().Be(2, "dos documentos valen mas que un error por el tercero");

        destino.Position = 0;
        using var zip = new ZipArchive(destino, ZipArchiveMode.Read);
        zip.Entries.Should().HaveCount(2);
    }

    // ── Utilidades de siembra ──────────────────────────────────────────────────────────────────

    private Guid Sembrar(int generadas, int errores)
    {
        var lote = Guid.CreateVersion7();
        InsertarLote(lote, generadas + errores);

        var fila = 1;
        for (var i = 0; i < generadas; i++, fila++)
        {
            InsertarGenerada(lote, fila, $"certificado_rues_fila-{fila}.pdf");
        }

        for (var i = 0; i < errores; i++, fila++)
        {
            _documents.Rows.Add(new StandaloneDocument
            {
                Id = Guid.CreateVersion7(),
                TenantId = Tenant,
                CreatedByUserId = Usuario,
                DocumentType = StandaloneDocumentType.CertificadoRues,
                Status = StandaloneDocumentStatus.Error,
                ErrorCode = "VB-02",
                ErrorField = "placa",
                ValidationErrors =
                    """[{"code":"VB-02","field":"placa","message":"La placa no tiene el formato exigido."}]""",
                BatchId = lote,
                RowNumber = fila,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        return lote;
    }

    private void InsertarLote(Guid id, int total)
    {
        _batches.Rows.Add(new StandaloneDocumentBatch
        {
            Id = id,
            TenantId = Tenant,
            CreatedByUserId = Usuario,
            Status = StandaloneDocumentBatchStatus.Completed,
            SourceFilename = "lote.xlsx",
            SourceStoragePath = $"fm://{Tenant}/generacion_documental_lote/{id}/lote.xlsx",
            SourceSha256 = new string('a', 64),
            TotalItems = total,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private void InsertarGenerada(Guid lote, int fila, string filename)
    {
        var path = $"fm://{Tenant}/generacion_documental/{lote}/{fila}/{filename}";
        _storage.Contenidos[path] = System.Text.Encoding.UTF8.GetBytes($"%PDF-1.7 documento fila {fila}");

        _documents.Rows.Add(new StandaloneDocument
        {
            Id = Guid.CreateVersion7(),
            TenantId = Tenant,
            CreatedByUserId = Usuario,
            DocumentType = StandaloneDocumentType.CertificadoRues,
            Status = StandaloneDocumentStatus.Generated,
            StoragePath = path,
            StorageSha256 = new string('b', 64),
            SizeBytes = 32,
            Filename = filename,
            BatchId = lote,
            RowNumber = fila,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }
}
