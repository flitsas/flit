using System.Security.Claims;
using System.Text.Json;
using Flit.Admin.Application.GeneracionDocumental.GenerateTransferencia;
using Flit.Api.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// HU #12207 — contrato HTTP de <c>POST /api/v1/admin/generacion-documental/transferencia/generate</c>.
///
/// <para>El delegate se invoca directamente y su <see cref="IResult"/> se EJECUTA sobre un
/// <see cref="DefaultHttpContext"/>: así se comprueban el código, el <c>Content-Type</c> y el cuerpo
/// reales.</para>
///
/// Uso de ejemplo:
/// <code>
/// var result = await AdminGeneracionDocumentalEndpoints.GenerateTransferenciaAsync(ctx, req, handler, ct);
/// await result.ExecuteAsync(ctx);
/// </code>
/// </summary>
public sealed class AdminGeneracionDocumentalTransferenciaEndpointTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Usuario = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly FakeStandaloneDocumentRepository _repo = new();
    private readonly FakeStandaloneTransferGenerator _generator = new();
    private readonly FakeStandaloneDocumentStorage _storage = new();

    private GenerateTransferenciaHandler Generar => new(_repo, _generator, _storage);

    private static AdminGeneracionDocumentalEndpoints.TransferenciaRequest Request(
        IReadOnlyList<string>? escenarios = null,
        string? placa = "ABC123",
        string? tituloJuridico = "COMPRAVENTA",
        string? precioLetras = "VEINTE MILLONES DE PESOS",
        string? precioNumeros = "20.000.000",
        string? documentoAdquirente = "10000002",
        bool gravamenActivo = false,
        bool tieneLevantamiento = false) => new(
        escenarios ?? ["A"],
        null,
        new AdminGeneracionDocumentalEndpoints.TransferenciaVehiculoRequest(
            placa, "MARCA DE PRUEBA", "LINEA DE PRUEBA", "2020", "AUTOMOVIL", "SEDAN", "BLANCO",
            "MOT0000001", "CHA0000001", "SER0000001", "PARTICULAR", "11223344",
            "SECRETARIA DE MOVILIDAD DE PRUEBA"),
        new AdminGeneracionDocumentalEndpoints.TransferenciaParteRequest(
            "PJ", "EMPRESA TRANSFERENTE SAS", "NIT", "900123456", null, "CIUDAD DE PRUEBA",
            "REPRESENTANTE DE PRUEBA", "10000001"),
        new AdminGeneracionDocumentalEndpoints.TransferenciaParteRequest(
            "PN", "PERSONA ADQUIRENTE DE PRUEBA", "CC", documentoAdquirente, null,
            "OTRA CIUDAD DE PRUEBA", null, null),
        new AdminGeneracionDocumentalEndpoints.TransferenciaNegocioRequest(
            tituloJuridico, null, precioLetras, precioNumeros, null, "Transferencia electronica",
            "TRANSFERENTE", "COMPARTIDOS", "ADQUIRENTE", "CIUDAD DE PRUEBA", new DateOnly(2026, 9, 9)),
        new AdminGeneracionDocumentalEndpoints.TransferenciaGravamenRequest(
            gravamenActivo, tieneLevantamiento),
        new AdminGeneracionDocumentalEndpoints.TransferenciaRegimenRequest(true, [], null));

    /// <summary>La generación responde JSON con cualquier Accept; nunca el binario (decisión del PO).</summary>
    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/json")]
    [InlineData("*/*")]
    public async Task Generate_RespondeJsonConCualquierAccept(string accept)
    {
        var ctx = NewContext(accept);

        var result = await AdminGeneracionDocumentalEndpoints.GenerateTransferenciaAsync(
            ctx, Request(), Generar, TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        ctx.Response.ContentType.Should().StartWith("application/json");
        ctx.Response.ContentType.Should().NotContain("application/pdf");
        body.Should().NotContain("%PDF");
        body.Should().NotContain("fm://");

        using var json = JsonDocument.Parse(body);
        json.RootElement.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["id", "status", "advisories"]);
        json.RootElement.GetProperty("status").GetString().Should().Be("generated");
    }

    /// <summary>VB-05 — cero o más de un escenario responde 422 con el código.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("A,B")]
    public async Task SinEscenarioOConVarios_Responde422ConVb05(string escenarios)
    {
        var ctx = NewContext();
        string[] lista = escenarios.Length == 0 ? [] : escenarios.Split(',');

        var result = await AdminGeneracionDocumentalEndpoints.GenerateTransferenciaAsync(
            ctx, Request(lista), Generar, TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        body.Should().Contain("VB-05");
        _repo.Rows.Should().BeEmpty();
    }

    /// <summary>
    /// CF-09 — cada error trae código, campo y mensaje, y ninguno refleja el valor capturado.
    /// </summary>
    [Fact]
    public async Task LosErroresTraenCodigoCampoYMensajeSinElValorCapturado()
    {
        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.GenerateTransferenciaAsync(
            ctx,
            Request(placa: "PL@CA!", documentoAdquirente: "900123456", tituloJuridico: null),
            Generar,
            TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);

        using var json = JsonDocument.Parse(body);
        var errores = json.RootElement.GetProperty("errors").EnumerateArray().ToList();

        errores.Select(e => e.GetProperty("code").GetString()).Should().Contain(
            ["VB-02", "VB-06", "VB-A-07"]);

        foreach (var error in errores)
        {
            error.GetProperty("field").GetString().Should().NotBeNullOrWhiteSpace();
            error.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
        }

        body.Should().NotContain("PL@CA!");
        body.Should().NotContain("900123456");
        body.Should().NotContain("EMPRESA TRANSFERENTE SAS");
    }

    [Fact]
    public async Task GravamenActivoSinLevantamiento_Responde422ConVbA04()
    {
        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.GenerateTransferenciaAsync(
            ctx, Request(gravamenActivo: true), Generar, TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        body.Should().Contain("VB-A-04");
    }

    [Fact]
    public async Task CompraventaSinPrecio_Responde422ConVbA06()
    {
        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.GenerateTransferenciaAsync(
            ctx, Request(precioLetras: null, precioNumeros: null), Generar,
            TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        body.Should().Contain("VB-A-06");
    }

    /// <summary>Las advisory salen en el 200 para que la interfaz las muestre como aviso.</summary>
    [Fact]
    public async Task LaRespuestaExitosaTraeLasAdvisoryYNingunError()
    {
        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.GenerateTransferenciaAsync(
            ctx, Request(), Generar, TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        using var json = JsonDocument.Parse(body);
        var advisories = json.RootElement.GetProperty("advisories").EnumerateArray().ToList();

        advisories.Should().NotBeEmpty();
        advisories.Select(a => a.GetProperty("code").GetString())
            .Should().Contain(["VB-01", "VB-A-08", "VB-A-09", "VB-A-10"]);
        body.Should().NotContain("\"errors\"");
    }

    /// <summary>El escenario B se reconoce pero todavía no se emite: es alcance de HU-06.</summary>
    [Fact]
    public async Task EscenarioB_Responde422SinPersistirNada()
    {
        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.GenerateTransferenciaAsync(
            ctx, Request(["B"]), Generar, TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        body.Should().Contain("scenario_not_implemented");
        _repo.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task SinClaimDeTenant_Responde401()
    {
        var ctx = NewContext(conTenant: false);

        var result = await AdminGeneracionDocumentalEndpoints.GenerateTransferenciaAsync(
            ctx, Request(), Generar, TestContext.Current.CancellationToken);

        await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    private static DefaultHttpContext NewContext(string? accept = null, bool conTenant = true)
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

        return ctx;
    }

    private static async Task<string> Execute(IResult result, DefaultHttpContext ctx)
    {
        await result.ExecuteAsync(ctx);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        return await new StreamReader(ctx.Response.Body).ReadToEndAsync();
    }
}
