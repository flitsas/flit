using System.Security.Claims;
using System.Text.Json;
using Flit.Admin.Application.GeneracionDocumental.Download;
using Flit.Admin.Application.GeneracionDocumental.List;
using Flit.Admin.Domain.GeneracionDocumental;
using Flit.Api.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// HU #12204 — aislamiento y contrato HTTP del historial y de la descarga (CF-19 / CF-20 / R3).
///
/// <para><b>Estos tests son de AUTORIZACIÓN sobre el endpoint, no de RLS.</b> La política RLS de
/// <c>admin.standalone_documents</c> no aísla nada en este repo (no hay
/// <c>FORCE ROW LEVEL SECURITY</c> y la aplicación conecta como owner, que la bypassa). Un test que
/// «pasara» por RLS pasaría igual sin ella y daría falsa seguridad: lo que se ejercita aquí es el
/// filtro por tenant del repositorio más el ownership check de la ruta.</para>
///
/// Uso de ejemplo:
/// <code>
/// var ctx = NewContext(Tenant);
/// var result = await AdminGeneracionDocumentalEndpoints.DownloadDocumentoAsync(ctx, id, handler, ct);
/// await result.ExecuteAsync(ctx); // ctx.Response.StatusCode == 404 para un documento ajeno
/// </code>
/// </summary>
public sealed class StandaloneDocumentAuthorizationTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtroTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Autor = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DocumentoId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private readonly FakeStandaloneDocumentRepository _repo = new();
    private readonly FakeStandaloneDocumentStorage _storage = new();

    private GetStandaloneDocumentDownloadHandler Descargar => new(_repo, _storage);

    private ListStandaloneDocumentsHandler Listar => new(_repo);

    // ── Descarga cross-tenant: 404, no 403 con detalle ──────────────────────────────

    [Fact]
    public async Task Download_DeOtroTenant_Responde404SinRevelarNada()
    {
        Sembrar(OtroTenant, StandaloneDocumentStatus.Generated);
        var ctx = NewContext(Tenant);

        var result = await AdminGeneracionDocumentalEndpoints.DownloadDocumentoAsync(
            ctx, DocumentoId, Descargar, TestContext.Current.CancellationToken);
        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        ctx.Response.StatusCode.Should().NotBe(StatusCodes.Status403Forbidden);
        body.Should().NotContain("http", "no sale ninguna presigned URL");
        body.Should().NotContain("fm://", "no sale la ruta de storage");
        body.Should().NotContain("generated", "no se revela el estado del documento ajeno");
        _storage.Presigned.Should().BeEmpty();
    }

    /// <summary>
    /// CF-20: SuperAdmin ve metadata global, pero descargar contenido ajeno le responde 404 igual.
    /// El rol no es una llave de descarga.
    /// </summary>
    [Fact]
    public async Task Download_SuperAdminSobreDocumentoAjeno_TambienResponde404()
    {
        Sembrar(OtroTenant, StandaloneDocumentStatus.Generated);
        var ctx = NewContext(Tenant, superAdmin: true);

        var result = await AdminGeneracionDocumentalEndpoints.DownloadDocumentoAsync(
            ctx, DocumentoId, Descargar, TestContext.Current.CancellationToken);
        await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        _storage.Presigned.Should().BeEmpty();
    }

    [Fact]
    public async Task Download_DeMiTenantGenerado_Responde200ConUrlYVencimiento()
    {
        Sembrar(Tenant, StandaloneDocumentStatus.Generated);
        var ctx = NewContext(Tenant);

        var result = await AdminGeneracionDocumentalEndpoints.DownloadDocumentoAsync(
            ctx, DocumentoId, Descargar, TestContext.Current.CancellationToken);
        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        ctx.Response.ContentType.Should().StartWith("application/json");

        using var json = JsonDocument.Parse(body);
        json.RootElement.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["url", "expiresAt"], "el contrato es { url, expiresAt } y nada más");
    }

    [Fact]
    public async Task Download_DeDocumentoEnError_Responde409SinUrl()
    {
        Sembrar(Tenant, StandaloneDocumentStatus.Error);
        var ctx = NewContext(Tenant);

        var result = await AdminGeneracionDocumentalEndpoints.DownloadDocumentoAsync(
            ctx, DocumentoId, Descargar, TestContext.Current.CancellationToken);
        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        body.Should().NotContain("http");
        _storage.Presigned.Should().BeEmpty();
    }

    [Fact]
    public async Task Download_SinClaimTenant_Responde401()
    {
        Sembrar(Tenant, StandaloneDocumentStatus.Generated);
        var ctx = NewContext(Tenant, conTenant: false);

        var result = await AdminGeneracionDocumentalEndpoints.DownloadDocumentoAsync(
            ctx, DocumentoId, Descargar, TestContext.Current.CancellationToken);
        await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        _storage.Presigned.Should().BeEmpty();
    }

    // ── Listado: nada de snapshot en el cuerpo, ni siquiera con filas que lo tienen ──

    [Fact]
    public async Task List_NoSerializaDocumentSnapshotNiRutasDeStorage()
    {
        _repo.Rows.Add(new StandaloneDocument
        {
            Id = DocumentoId,
            TenantId = Tenant,
            CreatedByUserId = Autor,
            DocumentType = StandaloneDocumentType.TransferenciaDominioGenerada,
            Scenario = "A",
            Status = StandaloneDocumentStatus.Generated,
            StoragePath = "fm://tenant/transferencia.pdf",
            StorageSha256 = new string('a', 64),
            Filename = "transferencia.pdf",
            DocumentSnapshot = "{\"adquirente\":{\"nombre\":\"NOMBRE COMPLETO\",\"direccion\":\"CL 1 # 2-3\"}}",
            RuesSnapshot = "{\"fields\":{\"rues_razon_social\":\"EMPRESA SAS\"}}",
            CreatedAt = new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero),
        });

        var ctx = NewContext(Tenant);
        var result = await AdminGeneracionDocumentalEndpoints.ListDocumentosAsync(
            ctx, Listar, TestContext.Current.CancellationToken);
        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        body.Should().Contain("transferencia_dominio_generada");
        body.Should().NotContain("documentSnapshot");
        body.Should().NotContain("ruesSnapshot");
        body.Should().NotContain("NOMBRE COMPLETO", "el snapshot es PII alta y no sale en el listado");
        body.Should().NotContain("fm://");
        body.Should().NotContain(new string('a', 64), "el hash del binario tampoco viaja en el listado");
    }

    [Fact]
    public async Task List_NoTraeFilasDeOtroTenant()
    {
        Sembrar(OtroTenant, StandaloneDocumentStatus.Generated);
        var ctx = NewContext(Tenant);

        var result = await AdminGeneracionDocumentalEndpoints.ListDocumentosAsync(
            ctx, Listar, TestContext.Current.CancellationToken);
        var body = await Execute(result, ctx);

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("total").GetInt32().Should().Be(0);
        json.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task List_ConFechaMalFormada_Responde400()
    {
        var ctx = NewContext(Tenant);
        ctx.Request.QueryString = new QueryString("?dateFrom=ayer");

        var result = await AdminGeneracionDocumentalEndpoints.ListDocumentosAsync(
            ctx, Listar, TestContext.Current.CancellationToken);
        await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// «Hasta el 9» incluye lo generado el 9 a cualquier hora: una fecha sin hora se traduce al
    /// límite superior exclusivo del día siguiente. Sin esto, el filtro perdería el último día.
    /// </summary>
    [Fact]
    public async Task List_FiltroHasta_IncluyeElDiaCompleto()
    {
        _repo.Rows.Add(new StandaloneDocument
        {
            Id = DocumentoId,
            TenantId = Tenant,
            CreatedByUserId = Autor,
            DocumentType = StandaloneDocumentType.CertificadoRues,
            Status = StandaloneDocumentStatus.Generated,
            CreatedAt = new DateTimeOffset(2026, 9, 9, 23, 30, 0, TimeSpan.Zero),
        });

        var ctx = NewContext(Tenant);
        ctx.Request.QueryString = new QueryString("?dateFrom=2026-09-01&dateTo=2026-09-09");

        var result = await AdminGeneracionDocumentalEndpoints.ListDocumentosAsync(
            ctx, Listar, TestContext.Current.CancellationToken);
        var body = await Execute(result, ctx);

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("total").GetInt32().Should().Be(1);
    }

    // ── Contrato: autorización por permiso ──────────────────────────────────────────

    [Fact]
    public void ElHistorialYLaDescargaExigenElPermisoDeLectura()
    {
        var fuente = File.ReadAllText(EndpointsSourcePath());

        System.Text.RegularExpressions.Regex
            .Matches(fuente, @"\.RequirePermission\(""generacion-documental\.read""\)")
            // HU #12210 suma GET /lotes/plantilla: descargar la plantilla es leer, no generar.
            .Should().HaveCount(3, "el listado, la descarga y la plantilla del lote son de .read");

        fuente.Should().NotContain("SuperAdminPolicy");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    private void Sembrar(Guid tenant, string status)
    {
        var generado = status == StandaloneDocumentStatus.Generated;
        _repo.Rows.Add(new StandaloneDocument
        {
            Id = DocumentoId,
            TenantId = tenant,
            CreatedByUserId = Autor,
            DocumentType = StandaloneDocumentType.CertificadoRues,
            Status = status,
            ErrorCode = status == StandaloneDocumentStatus.Error ? "rues_not_found" : null,
            StoragePath = generado ? "fm://tenant/certificado.pdf" : null,
            StorageSha256 = generado ? new string('a', 64) : null,
            Filename = generado ? "certificado.pdf" : null,
            CreatedAt = new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero),
        });
    }

    private static DefaultHttpContext NewContext(Guid tenantId, bool superAdmin = false, bool conTenant = true)
    {
        var claims = new List<Claim> { new("sub", Autor.ToString()) };
        if (conTenant)
        {
            claims.Add(new Claim("tenant_id", tenantId.ToString()));
        }

        if (superAdmin)
        {
            claims.Add(new Claim("role", "SuperAdmin"));
        }

        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth", "name", "role")),
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

    private static string EndpointsSourcePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(
            dir!.FullName, "services", "core-api", "src", "Flit.Api", "Endpoints",
            "AdminGeneracionDocumentalEndpoints.cs");
    }
}
