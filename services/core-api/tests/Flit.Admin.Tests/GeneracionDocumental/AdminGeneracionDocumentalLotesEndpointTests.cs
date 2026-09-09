using System.Security.Claims;
using System.Text.Json;
using Flit.Admin.Application.GeneracionDocumental.Batches;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;
using Flit.Api.Endpoints;
using Flit.Infrastructure.Documents.Standalone;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// Contrato HTTP de los lotes XLSX (HU #12210): <c>GET /lotes/plantilla</c> y <c>POST /lotes</c>.
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// var ctx = NewContext();
/// var result = await AdminGeneracionDocumentalEndpoints.CrearLoteAsync(ctx, handler, ct);
/// await result.ExecuteAsync(ctx); // ctx.Response.StatusCode == 202
/// </code>
///
/// <para>Los delegates se invocan directamente y su <see cref="IResult"/> se EJECUTA sobre un
/// <see cref="DefaultHttpContext"/>: se comprueban el código, el <c>Content-Type</c> y el cuerpo
/// reales, no la intención del código.</para>
/// </summary>
public sealed class AdminGeneracionDocumentalLotesEndpointTests
{
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Usuario = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly FakeStandaloneDocumentBatchRepository _batches = new();
    private readonly FakeStandaloneDocumentStorage _storage = new();

    private CreateBatchHandler Handler() =>
        new(_batches, new StandaloneDocumentXlsxParser(), _storage);

    private static byte[] LoteDe(int filas) => BatchXlsxBuilder.ConPlantillaV1(
        [.. Enumerable.Range(0, filas).Select(_ => BatchXlsxBuilder.Fila(
            XlsxCellKind.Shared,
            (StandaloneBatchTemplate.DocumentType, StandaloneDocumentType.CertificadoRues),
            (StandaloneBatchTemplate.Nit, "900123456")))]);

    // ── GET /lotes/plantilla ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Plantilla_DevuelveElXlsxConSuMimeReal()
    {
        var ctx = NewContext();

        var result = AdminGeneracionDocumentalEndpoints.DescargarPlantillaLoteAsync(
            new StandaloneDocumentXlsxTemplate());

        await result.ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        ctx.Response.ContentType.Should().Be(XlsxMime);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var bytes = new byte[4];
        _ = await ctx.Response.Body.ReadAsync(bytes, TestContext.Current.CancellationToken);
        bytes.Should().BeEquivalentTo(new byte[] { 0x50, 0x4B, 0x03, 0x04 }, "un XLSX es un paquete ZIP");
    }

    // ── POST /lotes ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CrearLote_ConArchivoConforme_Responde202ConBatchIdEstadoYTotal()
    {
        var ctx = NewContext(archivo: LoteDe(3));

        var result = await AdminGeneracionDocumentalEndpoints.CrearLoteAsync(
            ctx, Handler(), TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        ctx.Response.ContentType.Should().StartWith("application/json");

        using var json = JsonDocument.Parse(body);
        json.RootElement.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["batchId", "status", "total"], "el contrato es ese y nada más");
        json.RootElement.GetProperty("status").GetString().Should().Be(StandaloneDocumentBatchStatus.Queued);
        json.RootElement.GetProperty("total").GetInt32().Should().Be(3);

        body.Should().NotContain("fm://", "la ruta del XLSX fuente no sale en la respuesta");
    }

    [Fact]
    public async Task CrearLote_Con101Filas_Responde422ConTooManyRows()
    {
        var ctx = NewContext(archivo: LoteDe(101));

        var result = await AdminGeneracionDocumentalEndpoints.CrearLoteAsync(
            ctx, Handler(), TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        body.Should().Contain(StandaloneBatchParseError.TooManyRows);
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task CrearLote_ConArchivoQueNoEsXlsx_Responde422YNoPersisteElArchivo()
    {
        var ctx = NewContext(archivo: "%PDF-1.7 no es una hoja de calculo"u8.ToArray());

        var result = await AdminGeneracionDocumentalEndpoints.CrearLoteAsync(
            ctx, Handler(), TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        body.Should().Contain(StandaloneBatchParseError.InvalidFile);
        _storage.Saved.Should().BeEmpty("el MIME real se verifica antes de tocar storage");
    }

    [Fact]
    public async Task CrearLote_ConLaMismaClaveDeIdempotencia_Responde200ConElLoteExistente()
    {
        var handler = Handler();

        var primero = await AdminGeneracionDocumentalEndpoints.CrearLoteAsync(
            NewContext(archivo: LoteDe(2), idempotencyKey: "K-1"),
            handler,
            TestContext.Current.CancellationToken);
        _ = primero;

        var ctx = NewContext(archivo: LoteDe(2), idempotencyKey: "K-1");
        var repetido = await AdminGeneracionDocumentalEndpoints.CrearLoteAsync(
            ctx, handler, TestContext.Current.CancellationToken);

        var body = await Execute(repetido, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        body.Should().Contain(_batches.Rows.Single().Id.ToString());
        _batches.Rows.Should().ContainSingle();
        _storage.Saved.Should().ContainSingle();
    }

    [Fact]
    public async Task CrearLote_SinArchivo_Responde400()
    {
        var ctx = NewContext(archivo: null);

        var result = await AdminGeneracionDocumentalEndpoints.CrearLoteAsync(
            ctx, Handler(), TestContext.Current.CancellationToken);

        await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task CrearLote_SinClaimDeTenant_Responde401YNoPersisteNada()
    {
        var ctx = NewContext(archivo: LoteDe(1), conTenant: false);

        var result = await AdminGeneracionDocumentalEndpoints.CrearLoteAsync(
            ctx, Handler(), TestContext.Current.CancellationToken);

        await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        _batches.Rows.Should().BeEmpty();
        _storage.Saved.Should().BeEmpty();
    }

    // ── Utilidades ─────────────────────────────────────────────────────────────────────────────

    private static DefaultHttpContext NewContext(
        byte[]? archivo = null,
        string? idempotencyKey = null,
        bool conTenant = true)
    {
        var claims = new List<Claim> { new("sub", Usuario.ToString()) };
        if (conTenant)
        {
            claims.Add(new Claim("tenant_id", Tenant.ToString()));
        }

        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
            RequestServices = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider(),
        };

        ctx.Response.Body = new MemoryStream();
        ctx.Request.ContentType = "multipart/form-data; boundary=prueba";

        var archivos = new FormFileCollection();
        if (archivo is not null)
        {
            var contenido = new MemoryStream(archivo);
            archivos.Add(new FormFile(contenido, 0, archivo.Length, "file", "lote.xlsx")
            {
                Headers = new HeaderDictionary(),
            });
        }

        ctx.Request.Form = new FormCollection(new Dictionary<string, StringValues>(), archivos);

        if (idempotencyKey is not null)
        {
            ctx.Request.Headers["Idempotency-Key"] = idempotencyKey;
        }

        return ctx;
    }

    private static async Task<string> Execute(IResult result, DefaultHttpContext ctx)
    {
        await result.ExecuteAsync(ctx);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        return await new StreamReader(ctx.Response.Body).ReadToEndAsync();
    }
}
