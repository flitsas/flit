using System.Security.Claims;
using System.Text.Json;
using Flit.Admin.Application.GeneracionDocumental.GenerateRues;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Api.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// HU #12203 — contrato HTTP de <c>POST /api/v1/admin/generacion-documental/rues/*</c>.
///
/// <para>Los delegates se invocan directamente (son <c>internal</c> y el ensamblado de la API
/// declara <c>InternalsVisibleTo</c>) y su <see cref="IResult"/> se EJECUTA sobre un
/// <see cref="DefaultHttpContext"/>: así se comprueba el <c>Content-Type</c> y el cuerpo reales, no
/// la intención del código.</para>
///
/// Uso de ejemplo:
/// <code>
/// var ctx = NewContext(accept: "application/pdf");
/// var result = await AdminGeneracionDocumentalEndpoints.GenerateRuesAsync(ctx, request, handler, ct);
/// await result.ExecuteAsync(ctx); // ctx.Response.ContentType == "application/json; charset=utf-8"
/// </code>
/// </summary>
public sealed class AdminGeneracionDocumentalEndpointsTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Usuario = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly FakeStandaloneDocumentRepository _repo = new();
    private readonly FakeStandaloneRuesCompanyLookup _lookup = new();
    private readonly FakeStandaloneRuesCertificateRenderer _renderer = new();
    private readonly FakeStandaloneDocumentStorage _storage = new();

    private GenerateRuesDocumentHandler Generar => new(_repo, _lookup, _renderer, _storage);

    // ── Contrato de respuesta: json siempre, pdf nunca ──────────────────────────────

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/json")]
    [InlineData("*/*")]
    public async Task Generate_RespondeJsonConCualquierAccept(string accept)
    {
        var ctx = NewContext(accept: accept);

        var result = await AdminGeneracionDocumentalEndpoints.GenerateRuesAsync(
            ctx, new AdminGeneracionDocumentalEndpoints.RuesRequest("900123456"), Generar,
            TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        ctx.Response.ContentType.Should().StartWith("application/json");
        ctx.Response.ContentType.Should().NotContain("application/pdf");

        using var json = JsonDocument.Parse(body);
        json.RootElement.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["id", "status"], "el contrato es { id, status } y nada más");
        json.RootElement.GetProperty("status").GetString().Should().Be("generated");
        Guid.TryParse(json.RootElement.GetProperty("id").GetString(), out _).Should().BeTrue();
    }

    [Fact]
    public async Task Generate_NuncaDevuelveElBinarioNiUnaRutaDeStorage()
    {
        var ctx = NewContext(accept: "application/pdf");

        var result = await AdminGeneracionDocumentalEndpoints.GenerateRuesAsync(
            ctx, new AdminGeneracionDocumentalEndpoints.RuesRequest("900123456"), Generar,
            TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        result.Should().NotBeOfType<Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult>();
        body.Should().NotContain("fm://", "ni la ruta de storage ni el hash salen en la respuesta");
        body.Should().NotContain("%PDF");
    }

    // ── NIT sin coincidencia → 422 con código normalizado ───────────────────────────

    [Fact]
    public async Task Generate_NitInexistente_Responde422SinArchivo()
    {
        _lookup.Result = new StandaloneRuesLookupResult(
            false, new Dictionary<string, string?>(), DateTimeOffset.UtcNow, null);
        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.GenerateRuesAsync(
            ctx, new AdminGeneracionDocumentalEndpoints.RuesRequest("900999999"), Generar,
            TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        body.Should().Contain("rues_not_found");
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Generate_ProveedorCaido_Responde502()
    {
        _lookup.Result = new StandaloneRuesLookupResult(
            false, new Dictionary<string, string?>(), DateTimeOffset.UtcNow, "provider_unavailable");
        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.GenerateRuesAsync(
            ctx, new AdminGeneracionDocumentalEndpoints.RuesRequest("900123456"), Generar,
            TestContext.Current.CancellationToken);

        await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }

    [Fact]
    public async Task Generate_NitInvalido_Responde400()
    {
        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.GenerateRuesAsync(
            ctx, new AdminGeneracionDocumentalEndpoints.RuesRequest("abc"), Generar,
            TestContext.Current.CancellationToken);

        await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        _repo.Rows.Should().BeEmpty();
    }

    // ── Idempotencia por cabecera ───────────────────────────────────────────────────

    [Fact]
    public async Task Generate_ConIdempotencyKeyRepetida_DevuelveElMismoIdSinConsultar()
    {
        var primera = NewContext(idempotencyKey: "K-9");
        var r1 = await AdminGeneracionDocumentalEndpoints.GenerateRuesAsync(
            primera, new AdminGeneracionDocumentalEndpoints.RuesRequest("900123456"), Generar,
            TestContext.Current.CancellationToken);
        var body1 = await Execute(r1, primera);

        var segunda = NewContext(idempotencyKey: "K-9");
        var r2 = await AdminGeneracionDocumentalEndpoints.GenerateRuesAsync(
            segunda, new AdminGeneracionDocumentalEndpoints.RuesRequest("900123456"), Generar,
            TestContext.Current.CancellationToken);
        var body2 = await Execute(r2, segunda);

        using var j1 = JsonDocument.Parse(body1);
        using var j2 = JsonDocument.Parse(body2);
        j2.RootElement.GetProperty("id").GetString()
            .Should().Be(j1.RootElement.GetProperty("id").GetString());

        _lookup.Calls.Should().Be(1);
        _storage.Saved.Should().HaveCount(1);
    }

    // ── Token incompleto ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Generate_SinClaimTenant_Responde401()
    {
        var ctx = NewContext(conTenant: false);

        var result = await AdminGeneracionDocumentalEndpoints.GenerateRuesAsync(
            ctx, new AdminGeneracionDocumentalEndpoints.RuesRequest("900123456"), Generar,
            TestContext.Current.CancellationToken);

        await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        _lookup.Calls.Should().Be(0);
    }

    // ── Preview: 200 sin persistir ──────────────────────────────────────────────────

    [Fact]
    public async Task Preview_Responde200YNoPersisteNada()
    {
        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.PreviewRuesAsync(
            ctx, new AdminGeneracionDocumentalEndpoints.RuesRequest("900123456"),
            new PreviewRuesCompanyHandler(_lookup), TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        ctx.Response.ContentType.Should().StartWith("application/json");
        body.Should().Contain("rues_razon_social");

        _repo.Rows.Should().BeEmpty("el preview no crea ninguna fila en admin.standalone_documents");
        _storage.Saved.Should().BeEmpty();
    }

    // ── Autorización por permiso, no por policy de grupo ────────────────────────────

    [Fact]
    public void LasDosRutasExigenElPermisoDeGeneracion()
    {
        var fuente = File.ReadAllText(EndpointsSourcePath());

        fuente.Should().Contain(".RequirePermission(\"generacion-documental.generate\")");
        System.Text.RegularExpressions.Regex
            .Matches(fuente, @"\.RequirePermission\(""generacion-documental\.generate""\)")
            // HU #12206 sumo los tres /prefill/*, que tambien gastan consultas de pago;
            // HU #12207 suma /transferencia/generate.
            .Should().HaveCount(6, "preview, generate, los tres prellenados y la transferencia");

        // El módulo lo usa AdminCompany: una policy de grupo de SuperAdmin lo dejaría fuera.
        fuente.Should().NotContain("SuperAdminPolicy");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    private static DefaultHttpContext NewContext(
        string? accept = null,
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

        if (accept is not null)
        {
            ctx.Request.Headers.Accept = accept;
        }

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
