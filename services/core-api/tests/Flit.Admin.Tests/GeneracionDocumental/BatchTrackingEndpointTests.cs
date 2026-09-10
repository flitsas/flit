using System.Security.Claims;
using System.Text.Json;
using Flit.Admin.Application.GeneracionDocumental.Batches;
using Flit.Admin.Application.GeneracionDocumental.List;
using Flit.Admin.Domain.GeneracionDocumental;
using Flit.Api.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// Contrato HTTP del seguimiento de lote (HU #12211): <c>GET /lotes/{batchId}</c>,
/// <c>GET /lotes/{batchId}/items</c>, <c>GET /lotes/{batchId}/zip</c> y el filtro por lote del
/// historial.
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// var ctx = NewContext();
/// var result = await AdminGeneracionDocumentalEndpoints.ObtenerEstadoLoteAsync(ctx, batchId, handler, ct);
/// await result.ExecuteAsync(ctx); // ctx.Response.StatusCode == 200
/// </code>
///
/// <para>El aislamiento se prueba SOBRE EL ENDPOINT (404 cross-tenant), nunca apoyándose en la
/// política RLS: en este repositorio la RLS es decorativa —no hay <c>FORCE ROW LEVEL SECURITY</c> y
/// la aplicación conecta como owner—, así que un test que «pasara por RLS» pasaría igual sin ella.</para>
/// </summary>
public sealed class BatchTrackingEndpointTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtroTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Usuario = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly FakeStandaloneDocumentBatchRepository _batches = new();
    private readonly FakeStandaloneDocumentRepository _documents = new();
    private readonly FakeStandaloneDocumentStorage _storage = new();

    // ── GET /lotes/{batchId} — avance (CF-14 / CF-21) ──────────────────────────────────────────

    [Fact]
    public async Task Estado_DeLoteEnProceso_DevuelveContadoresRealesYNoTerminal()
    {
        var lote = Sembrar(StandaloneDocumentBatchStatus.Processing, generadas: 3, errores: 1, total: 10);
        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.ObtenerEstadoLoteAsync(
            ctx, lote, new GetBatchStatusHandler(_batches, _documents), TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("status").GetString().Should().Be(StandaloneDocumentBatchStatus.Processing);
        json.RootElement.GetProperty("total").GetInt32().Should().Be(10);
        json.RootElement.GetProperty("generated").GetInt32().Should()
            .Be(3, "los contadores se cuentan sobre las filas, no se leen de la cabecera");
        json.RootElement.GetProperty("errors").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("isTerminal").GetBoolean().Should()
            .BeFalse("mientras procesa, el cliente debe seguir sondeando");

        body.Should().NotContain("fm://", "la ruta del XLSX fuente no sale en el seguimiento");
    }

    [Theory]
    [InlineData(StandaloneDocumentBatchStatus.Completed)]
    [InlineData(StandaloneDocumentBatchStatus.PartialFailure)]
    [InlineData(StandaloneDocumentBatchStatus.Failed)]
    public async Task Estado_EnUnEstadoTerminal_LoDeclaraExplicitamente(string estadoTerminal)
    {
        var lote = Sembrar(estadoTerminal, generadas: 9, errores: 1, total: 10);
        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.ObtenerEstadoLoteAsync(
            ctx, lote, new GetBatchStatusHandler(_batches, _documents), TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("isTerminal").GetBoolean().Should()
            .BeTrue("es la senal con la que el polling se detiene (CF-14)");
    }

    [Fact]
    public async Task Estado_DePartialFailure_ExponeGeneradosYErroresParaMostrarElResultado()
    {
        var lote = Sembrar(StandaloneDocumentBatchStatus.PartialFailure, generadas: 9, errores: 1, total: 10);
        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.ObtenerEstadoLoteAsync(
            ctx, lote, new GetBatchStatusHandler(_batches, _documents), TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("status").GetString().Should()
            .Be(StandaloneDocumentBatchStatus.PartialFailure);
        json.RootElement.GetProperty("generated").GetInt32().Should().Be(9);
        json.RootElement.GetProperty("errors").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Estado_DeLoteDeOtroTenant_Responde404SinRevelarNada()
    {
        var lote = Sembrar(StandaloneDocumentBatchStatus.Completed, generadas: 2, errores: 0, total: 2);
        var ctx = NewContext(tenant: OtroTenant);

        var result = await AdminGeneracionDocumentalEndpoints.ObtenerEstadoLoteAsync(
            ctx, lote, new GetBatchStatusHandler(_batches, _documents), TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        body.Should().Be("""{"error":"not_found"}""", "el cuerpo no distingue «no existe» de «es ajeno»");
        body.Should().NotContain("total").And.NotContain("lote.xlsx");
    }

    // ── GET /lotes/{batchId}/items — detalle de errores (CF-13) ────────────────────────────────

    [Fact]
    public async Task Items_MuestranFilaTipoEstadoYElCodigoYCampoDelError()
    {
        var lote = Sembrar(StandaloneDocumentBatchStatus.PartialFailure, generadas: 1, errores: 1, total: 2);
        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.ListarItemsLoteAsync(
            ctx, lote, new ListBatchItemsHandler(_batches, _documents), TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);

        using var json = JsonDocument.Parse(body);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);

        var conError = items.Single(i => i.GetProperty("status").GetString() == StandaloneDocumentStatus.Error);
        conError.GetProperty("rowNumber").GetInt32().Should().Be(2);
        conError.GetProperty("errorCode").GetString().Should().Be("VB-02");
        conError.GetProperty("errorField").GetString().Should().Be("placa");

        var errores = conError.GetProperty("validationErrors").EnumerateArray().ToList();
        errores.Should().ContainSingle();
        errores[0].GetProperty("code").GetString().Should().Be("VB-02");
        errores[0].GetProperty("field").GetString().Should().Be("placa");
        errores[0].GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Items_NoExponenElValorCapturadoQueProdujoElError()
    {
        // La fila trajo esta placa; el detalle nombra el CAMPO, jamas su contenido.
        const string ValorCapturado = "XYZ-9999";

        var lote = Guid.CreateVersion7();
        InsertarLote(lote, StandaloneDocumentBatchStatus.PartialFailure, total: 1);
        _documents.Rows.Add(FilaEnError(lote, fila: 1, valorCapturado: ValorCapturado));

        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.ListarItemsLoteAsync(
            ctx, lote, new ListBatchItemsHandler(_batches, _documents), TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        body.Should().NotContain(ValorCapturado, "el detalle de errores no refleja el valor capturado (CF-13)");
        body.Should().Contain("VB-02").And.Contain("placa");
    }

    [Fact]
    public async Task Items_DeFilaSinTipificar_LlevanElErrorRealAunqueLaColumnaDigaCertificadoRues()
    {
        // Trampa del esquema (HU #12210): un document_type desconocido NO se puede persistir —los
        // CHECK solo admiten dos literales—, asi que la fila queda como certificado_rues y el error
        // real vive en validation_errors. La interfaz muestra el error, no la columna.
        var lote = Guid.CreateVersion7();
        InsertarLote(lote, StandaloneDocumentBatchStatus.PartialFailure, total: 1);

        _documents.Rows.Add(new StandaloneDocument
        {
            Id = Guid.CreateVersion7(),
            TenantId = Tenant,
            CreatedByUserId = Usuario,
            DocumentType = StandaloneDocumentType.CertificadoRues,
            Scenario = null,
            Status = StandaloneDocumentStatus.Error,
            ErrorCode = StandaloneDocumentBatchRunner.ErrorUnknownDocumentType,
            ErrorField = "document_type",
            ValidationErrors =
                """[{"code":"unknown_document_type","field":"document_type","message":"El tipo de documento declarado no existe."}]""",
            BatchId = lote,
            RowNumber = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.ListarItemsLoteAsync(
            ctx, lote, new ListBatchItemsHandler(_batches, _documents), TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        using var json = JsonDocument.Parse(body);
        var item = json.RootElement.GetProperty("items").EnumerateArray().Single();

        item.GetProperty("documentType").GetString().Should()
            .Be(StandaloneDocumentType.CertificadoRues, "la columna miente en esta fila, por diseno del esquema");
        item.GetProperty("validationErrors").EnumerateArray().Single()
            .GetProperty("code").GetString().Should()
            .Be(StandaloneDocumentBatchRunner.ErrorUnknownDocumentType, "el error real es lo que se muestra");
    }

    [Fact]
    public async Task Items_DeLoteDeOtroTenant_Responde404()
    {
        var lote = Sembrar(StandaloneDocumentBatchStatus.Completed, generadas: 3, errores: 0, total: 3);
        var ctx = NewContext(tenant: OtroTenant);

        var result = await AdminGeneracionDocumentalEndpoints.ListarItemsLoteAsync(
            ctx, lote, new ListBatchItemsHandler(_batches, _documents), TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        body.Should().NotContain("rowNumber").And.NotContain("fm://");
    }

    [Fact]
    public async Task Items_NoExponenSnapshotsNiRutasDeStorage()
    {
        var lote = Sembrar(StandaloneDocumentBatchStatus.Completed, generadas: 2, errores: 0, total: 2);
        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.ListarItemsLoteAsync(
            ctx, lote, new ListBatchItemsHandler(_batches, _documents), TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        body.Should().NotContain("fm://").And.NotContain("storagePath");
        body.Should().NotContain("documentSnapshot").And.NotContain("ruesSnapshot");
    }

    // ── GET /lotes/{batchId}/zip ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Zip_DeLoteConGenerados_RespondeApplicationZip()
    {
        var lote = Sembrar(StandaloneDocumentBatchStatus.PartialFailure, generadas: 9, errores: 1, total: 10);
        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.DescargarZipLoteAsync(
            ctx,
            lote,
            new DownloadBatchZipHandler(_batches, _documents, _storage),
            TestContext.Current.CancellationToken);

        await result.ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        ctx.Response.ContentType.Should().Be("application/zip");
        ctx.Response.Headers.ContentDisposition.ToString().Should()
            .Contain("attachment", "el ZIP se descarga, no se abre inline");

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var firma = new byte[2];
        _ = await ctx.Response.Body.ReadAsync(firma, TestContext.Current.CancellationToken);
        firma.Should().BeEquivalentTo(new byte[] { 0x50, 0x4B }, "un ZIP empieza por PK");
    }

    [Fact]
    public async Task Zip_DeLoteSinGenerados_Responde409ConExplicacionYNoConUnZipVacio()
    {
        var lote = Sembrar(StandaloneDocumentBatchStatus.Failed, generadas: 0, errores: 5, total: 5);
        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.DescargarZipLoteAsync(
            ctx,
            lote,
            new DownloadBatchZipHandler(_batches, _documents, _storage),
            TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        ctx.Response.ContentType.Should().StartWith("application/json");
        body.Should().Contain("no_documents");
        body.Should().Contain("no tiene documentos generados", "la respuesta explica que paso");
    }

    [Fact]
    public async Task Zip_DeLoteDeOtroTenant_Responde404SinPresignedNiPii()
    {
        var lote = Sembrar(StandaloneDocumentBatchStatus.Completed, generadas: 4, errores: 0, total: 4);
        var ctx = NewContext(tenant: OtroTenant);

        var result = await AdminGeneracionDocumentalEndpoints.DescargarZipLoteAsync(
            ctx,
            lote,
            new DownloadBatchZipHandler(_batches, _documents, _storage),
            TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        body.Should().Be("""{"error":"not_found"}""");
        _storage.Presigned.Should().BeEmpty();
        _storage.Abiertos.Should().BeEmpty();
    }

    [Fact]
    public async Task Zip_SinClaimDeTenant_Responde401()
    {
        var lote = Sembrar(StandaloneDocumentBatchStatus.Completed, generadas: 1, errores: 0, total: 1);
        var ctx = NewContext(conTenant: false);

        var result = await AdminGeneracionDocumentalEndpoints.DescargarZipLoteAsync(
            ctx,
            lote,
            new DownloadBatchZipHandler(_batches, _documents, _storage),
            TestContext.Current.CancellationToken);

        await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        _storage.Abiertos.Should().BeEmpty();
    }

    // ── CF-18: filtro por lote en el historial ─────────────────────────────────────────────────

    [Fact]
    public async Task Historial_FiltradoPorLote_SoloDevuelveLasFilasDeEseLote()
    {
        var loteA = Sembrar(StandaloneDocumentBatchStatus.Completed, generadas: 3, errores: 0, total: 3);
        _ = Sembrar(StandaloneDocumentBatchStatus.Completed, generadas: 2, errores: 0, total: 2);

        var ctx = NewContext();
        ctx.Request.QueryString = new QueryString($"?batchId={loteA}");

        var result = await AdminGeneracionDocumentalEndpoints.ListDocumentosAsync(
            ctx,
            new ListStandaloneDocumentsHandler(_documents),
            TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("total").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Historial_FiltroPorLote_ConviveConLosDeTipoFechaYUsuario()
    {
        var lote = Sembrar(StandaloneDocumentBatchStatus.PartialFailure, generadas: 4, errores: 2, total: 6);

        var ctx = NewContext();
        ctx.Request.QueryString = new QueryString(
            $"?batchId={lote}"
            + $"&documentType={StandaloneDocumentType.CertificadoRues}"
            + "&status=generated"
            + "&dateFrom=2020-01-01"
            + $"&userId={Usuario}");

        var result = await AdminGeneracionDocumentalEndpoints.ListDocumentosAsync(
            ctx,
            new ListStandaloneDocumentsHandler(_documents),
            TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("total").GetInt32().Should()
            .Be(4, "los cinco filtros se aplican a la vez con AND, no se excluyen");
    }

    [Fact]
    public async Task Historial_ConLoteDeOtroTenant_NoDevuelveNada()
    {
        var lote = Sembrar(StandaloneDocumentBatchStatus.Completed, generadas: 3, errores: 0, total: 3);

        var ctx = NewContext(tenant: OtroTenant);
        ctx.Request.QueryString = new QueryString($"?batchId={lote}");

        var result = await AdminGeneracionDocumentalEndpoints.ListDocumentosAsync(
            ctx,
            new ListStandaloneDocumentsHandler(_documents),
            TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("total").GetInt32().Should()
            .Be(0, "el filtro por lote no puede saltarse el WHERE tenant_id");
    }

    // ── Utilidades ─────────────────────────────────────────────────────────────────────────────

    private Guid Sembrar(string estado, int generadas, int errores, int total)
    {
        var lote = Guid.CreateVersion7();
        InsertarLote(lote, estado, total);

        var fila = 1;
        for (var i = 0; i < generadas; i++, fila++)
        {
            var path = $"fm://{Tenant}/generacion_documental/{lote}/{fila}/documento.pdf";
            _storage.Contenidos[path] = "%PDF-1.7"u8.ToArray();

            _documents.Rows.Add(new StandaloneDocument
            {
                Id = Guid.CreateVersion7(),
                TenantId = Tenant,
                CreatedByUserId = Usuario,
                DocumentType = StandaloneDocumentType.CertificadoRues,
                Status = StandaloneDocumentStatus.Generated,
                StoragePath = path,
                StorageSha256 = new string('b', 64),
                SizeBytes = 8,
                Filename = $"certificado_rues_{fila}.pdf",
                BatchId = lote,
                RowNumber = fila,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        for (var i = 0; i < errores; i++, fila++)
        {
            _documents.Rows.Add(FilaEnError(lote, fila, "ABC-123"));
        }

        return lote;
    }

    private static StandaloneDocument FilaEnError(Guid lote, int fila, string valorCapturado)
    {
        _ = valorCapturado; // Deliberadamente NO viaja a ninguna columna: es lo que el AC exige.

        return new StandaloneDocument
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
        };
    }

    private void InsertarLote(Guid id, string estado, int total)
    {
        _batches.Rows.Add(new StandaloneDocumentBatch
        {
            Id = id,
            TenantId = Tenant,
            CreatedByUserId = Usuario,
            Status = estado,
            SourceFilename = "lote.xlsx",
            SourceStoragePath = $"fm://{Tenant}/generacion_documental_lote/{id}/lote.xlsx",
            SourceSha256 = new string('a', 64),
            TotalItems = total,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static DefaultHttpContext NewContext(Guid? tenant = null, bool conTenant = true)
    {
        var claims = new List<Claim> { new("sub", Usuario.ToString()) };
        if (conTenant)
        {
            claims.Add(new Claim("tenant_id", (tenant ?? Tenant).ToString()));
        }

        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
            RequestServices = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider(),
        };

        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static async Task<string> Execute(IResult result, DefaultHttpContext ctx)
    {
        await result.ExecuteAsync(ctx);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        return await new StreamReader(ctx.Response.Body).ReadToEndAsync();
    }
}
