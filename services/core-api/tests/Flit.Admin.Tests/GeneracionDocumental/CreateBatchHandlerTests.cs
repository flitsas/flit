using Flit.Admin.Application.GeneracionDocumental.Batches;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;
using Flit.Infrastructure.Documents.Standalone;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// Carga de lote XLSX (HU #12210, CF-11/CF-16).
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// var handler = new CreateBatchHandler(batches, new StandaloneDocumentXlsxParser(), storage);
/// var result = await handler.HandleAsync(new CreateBatchCommand(tenant, user, "lote.xlsx", stream));
/// result.Outcome.Should().Be(CreateBatchOutcome.Accepted);
/// </code>
///
/// <para>El parser es el REAL, no un doble: lo que se verifica aquí es el orden de las operaciones
/// —idempotencia, validación, storage— y ese orden solo significa algo contra archivos de verdad.</para>
/// </summary>
public sealed class CreateBatchHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Usuario = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly FakeStandaloneDocumentBatchRepository _batches = new();
    private readonly FakeStandaloneDocumentStorage _storage = new();

    private CreateBatchHandler Handler() =>
        new(_batches, new StandaloneDocumentXlsxParser(), _storage);

    private static byte[] LoteDe(int filas) => BatchXlsxBuilder.ConPlantillaV1(
        [.. Enumerable.Range(0, filas).Select(_ => BatchXlsxBuilder.Fila(
            XlsxCellKind.Shared,
            (StandaloneBatchTemplate.DocumentType, StandaloneDocumentType.CertificadoRues),
            (StandaloneBatchTemplate.Nit, "900123456")))]);

    [Fact]
    public async Task HandleAsync_ConCienFilasConformes_EncolaElLoteConElTotal()
    {
        var result = await Handler().HandleAsync(
            new CreateBatchCommand(Tenant, Usuario, "lote.xlsx", new MemoryStream(LoteDe(100))),
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateBatchOutcome.Accepted);
        result.Status.Should().Be(StandaloneDocumentBatchStatus.Queued);
        result.Total.Should().Be(100);

        _batches.Rows.Should().ContainSingle();
        _batches.Rows[0].TenantId.Should().Be(Tenant, "el tenant sale del JWT, no del archivo");
        _batches.Rows[0].TemplateVersion.Should().Be(StandaloneBatchTemplate.Version);
        _storage.Saved.Should().ContainSingle("el XLSX fuente va a storage, nunca a una columna BYTEA");
    }

    [Fact]
    public async Task HandleAsync_Con101Filas_RechazaConTooManyRowsYNoPersisteElArchivo()
    {
        var result = await Handler().HandleAsync(
            new CreateBatchCommand(Tenant, Usuario, "lote.xlsx", new MemoryStream(LoteDe(101))),
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateBatchOutcome.Rejected);
        result.ErrorCode.Should().Be(StandaloneBatchParseError.TooManyRows);

        _batches.Rows.Should().BeEmpty();
        _storage.Saved.Should().BeEmpty("un archivo rechazado no deja basura en storage");
    }

    [Fact]
    public async Task HandleAsync_ConEncabezadoAjeno_RechazaConTemplateInvalidYNoPersisteElArchivo()
    {
        var header = StandaloneBatchTemplate.Headers.ToList();
        header[0] = "tipo";

        var result = await Handler().HandleAsync(
            new CreateBatchCommand(
                Tenant, Usuario, "otro.xlsx", new MemoryStream(BatchXlsxBuilder.Build(header, []))),
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateBatchOutcome.Rejected);
        result.ErrorCode.Should().Be(StandaloneBatchParseError.TemplateInvalid);
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ConArchivoQueNoEsXlsx_RechazaYNoPersisteElArchivo()
    {
        var result = await Handler().HandleAsync(
            new CreateBatchCommand(
                Tenant, Usuario, "lote.xlsx", new MemoryStream("%PDF-1.7 no es una hoja"u8.ToArray())),
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateBatchOutcome.Rejected);
        result.ErrorCode.Should().Be(StandaloneBatchParseError.InvalidFile);
        _storage.Saved.Should().BeEmpty("el MIME real se verifica ANTES de tocar storage");
        _batches.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ConLaMismaClaveDeIdempotencia_DevuelveElLoteExistenteSinCrearNada()
    {
        var handler = Handler();

        var primero = await handler.HandleAsync(
            new CreateBatchCommand(Tenant, Usuario, "lote.xlsx", new MemoryStream(LoteDe(3)), "K-1"),
            TestContext.Current.CancellationToken);

        var repetido = await handler.HandleAsync(
            new CreateBatchCommand(Tenant, Usuario, "lote.xlsx", new MemoryStream(LoteDe(3)), "K-1"),
            TestContext.Current.CancellationToken);

        repetido.Outcome.Should().Be(CreateBatchOutcome.AlreadyExists);
        repetido.BatchId.Should().Be(primero.BatchId);

        _batches.Rows.Should().ContainSingle("no se crea un lote nuevo");
        _storage.Saved.Should().ContainSingle("ni un archivo fuente nuevo");
    }

    [Fact]
    public async Task HandleAsync_ConLaMismaClaveEnOtroTenant_CreaUnLotePropio()
    {
        // La idempotencia es POR TENANT: dos compañías pueden usar la misma clave sin verse.
        var otroTenant = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var handler = Handler();

        await handler.HandleAsync(
            new CreateBatchCommand(Tenant, Usuario, "lote.xlsx", new MemoryStream(LoteDe(2)), "K-1"),
            TestContext.Current.CancellationToken);

        var otro = await handler.HandleAsync(
            new CreateBatchCommand(otroTenant, Usuario, "lote.xlsx", new MemoryStream(LoteDe(2)), "K-1"),
            TestContext.Current.CancellationToken);

        otro.Outcome.Should().Be(CreateBatchOutcome.Accepted);
        _batches.Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task HandleAsync_SinArchivo_RespondeInvalidRequest()
    {
        var result = await Handler().HandleAsync(
            new CreateBatchCommand(Tenant, Usuario, "lote.xlsx", null),
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateBatchOutcome.InvalidRequest);
        _storage.Saved.Should().BeEmpty();
    }
}
