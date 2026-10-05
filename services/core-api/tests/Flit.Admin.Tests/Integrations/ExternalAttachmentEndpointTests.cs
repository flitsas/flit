using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Admin.Domain.Integrations;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.ExternalSync;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Flit.Admin.Tests.Integrations;

/// <summary>
/// HU #13263 (Feature #13261, Épica #12741) — <c>POST /api/v1/external/tramites/{id}/adjuntos</c> contra el host real
/// (esquema externo, policy de escritura, multipart en streaming, problem+json, bitácora y cuota); el repositorio, el
/// almacenamiento y la matriz son dobles. El SQL real, el bloqueo de fila y el alcance están en <c>Flit.Integration.Tests</c>.
/// Cada prueba usa su propio <c>client_id</c> para no mezclar bitácora ni cuota.
/// </summary>
public sealed class ExternalAttachmentEndpointTests : IClassFixture<ExternalAttachmentEndpointTests.Factory>
{
    private static readonly Guid Tramite = Guid.CreateVersion7();
    private static readonly Guid Compania = Guid.CreateVersion7();
    private static readonly string Url = $"/api/v1/external/tramites/{Tramite}/adjuntos";
    private static readonly byte[] Pdf = "%PDF-1.4 comprobante sintetico"u8.ToArray();

    private readonly Factory _factory;

    public ExternalAttachmentEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Envio.Reiniciar(Target("asignado"));
        _factory.Almacen.Guardados.Clear();
        _factory.Almacen.Borrados.Clear();
        _factory.Almacen.Falla = null;
        _factory.Matriz.IncluyeLiquidacionImpuesto = true;
    }

    [Fact]
    public async Task AC1_Responde201ConElReciboDelContratoYSinCache()
    {
        var response = await Post(Url, Pdf, cliente: "adj-ac1");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await Json(response);
        body.EnumerateObject().Select(p => p.Name).Should()
            .BeEquivalentTo("adjuntoId", "tipo", "sha256", "reemplazoDe", "enMatriz", "pagadoMarcado");
        body.GetProperty("adjuntoId").GetGuid().Should().NotBeEmpty();
        body.GetProperty("tipo").GetString().Should().Be("liquidacion_impuesto");
        body.GetProperty("sha256").GetString().Should().Be(Convert.ToHexStringLower(SHA256.HashData(Pdf)));
        body.GetProperty("reemplazoDe").ValueKind.Should().Be(JsonValueKind.Null, "el campo se envía siempre, también null");
        body.GetProperty("enMatriz").GetBoolean().Should().BeTrue();
        body.GetProperty("pagadoMarcado").GetBoolean().Should().BeFalse();
        _factory.Envio.Escritos.Should().ContainSingle().Which.Filename.Should().Be("recibo.pdf");
    }

    [Fact]
    public async Task AC1_ElNombreDelArchivoSeLimpiaDeRutas()
    {
        var response = await Post(Url, Pdf, cliente: "adj-nombre", nombre: @"C:\Users\x\Descargas\..\recibo final.pdf");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _factory.Envio.Escritos.Should().ContainSingle().Which.Filename.Should().Be("recibo final.pdf");
    }

    [Fact]
    public async Task AC2_TipoFueraDeLaListaEsInvalidTipo()
    {
        await ShouldBeProblem(await Post(Url, Pdf, cliente: "adj-tipo", tipo: "factura"), HttpStatusCode.BadRequest, "invalid_tipo");
        await ShouldBeProblem(await Post(Url, Pdf, cliente: "adj-tipo", tipo: null), HttpStatusCode.BadRequest, "invalid_tipo");
        _factory.Envio.Escritos.Should().BeEmpty();
    }

    [Theory]
    [InlineData("application/msword")]
    [InlineData("text/plain")]
    [InlineData("application/octet-stream")]
    public async Task AC2_ElContentTypeDeLaParteDecideElMime(string mime)
    {
        await ShouldBeProblem(await Post(Url, Pdf, cliente: "adj-mime", mime: mime, nombre: "recibo.pdf"), HttpStatusCode.BadRequest, "invalid_mime");
        _factory.Envio.Escritos.Should().BeEmpty();
    }

    [Theory]
    [InlineData("image/jpeg", "recibo.jpg")]
    [InlineData("image/png", "recibo.png")]
    [InlineData("image/webp", "recibo.webp")]
    public async Task AC2_LosOtrosTiposMimeDelContratoSeAceptan(string mime, string nombre)
    {
        (await Post(Url, Pdf, cliente: "adj-mimes", mime: mime, nombre: nombre)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AC2_UnArchivoDeMasDe20MbRecibeProblemJsonFileTooLargeNoUn413()
    {
        var response = await Post(Url, new byte[(20 * 1024 * 1024) + 1], cliente: "adj-grande");

        await ShouldBeProblem(response, HttpStatusCode.BadRequest, "file_too_large");
        _factory.Almacen.Guardados.Should().BeEmpty();
    }

    [Fact]
    public async Task AC2_VeinteMegasExactosSeAceptan()
    {
        var bytes = new byte[20 * 1024 * 1024];
        bytes[0] = 7;

        (await Post(Url, bytes, cliente: "adj-limite")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AC2_SinArchivoOArchivoVacioOCuerpoQueNoEsMultipartEsMissingFile()
    {
        await ShouldBeProblem(await Post(Url, null, cliente: "adj-sin"), HttpStatusCode.BadRequest, "missing_file");
        await ShouldBeProblem(await Post(Url, [], cliente: "adj-vacio"), HttpStatusCode.BadRequest, "missing_file");

        using var json = Peticion(Url, "adj-json", [ExternalScopes.AttachmentsWrite]);
        json.Content = new StringContent("{\"tipo\":\"liquidacion_impuesto\"}", System.Text.Encoding.UTF8, "application/json");
        await ShouldBeProblem(await _factory.CreateClient().SendAsync(json, TestContext.Current.CancellationToken), HttpStatusCode.BadRequest, "missing_file");
    }

    [Theory]
    [InlineData("preasignacion", false)]
    [InlineData("entregado", false)]
    [InlineData("rechazado", true)]
    public async Task AC3_LosEstadosQueAceptanResponden201(string estado, bool subsanacion)
    {
        _factory.Envio.Reiniciar(Target(estado, subsanacion));

        (await Post(Url, Pdf, cliente: "adj-estado-ok")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("aprobado", false, true)]
    [InlineData("anulado", false, true)]
    [InlineData("revocado", false, true)]
    [InlineData("borrador", false, false)]
    [InlineData("preparado", false, false)]
    [InlineData("rechazado", false, false)]
    public async Task AC3_EstadoNoPermitido409LlevaEstadoYTerminalYNoArchiva(string estado, bool subsanacion, bool terminal)
    {
        _factory.Envio.Reiniciar(Target(estado, subsanacion));

        var response = await Post(Url, Pdf, cliente: "adj-estado");

        var body = await ShouldBeProblem(response, HttpStatusCode.Conflict, "not_allowed_in_state");
        body.GetProperty("estado").GetString().Should().Be(estado);
        body.GetProperty("terminal").GetBoolean().Should().Be(terminal);
        _factory.Almacen.Guardados.Should().BeEmpty();
        _factory.Envio.Escritos.Should().BeEmpty();
    }

    [Fact]
    public async Task AC3_LosOtrosProblemNoLlevanEstadoNiTerminal()
    {
        var body = await ShouldBeProblem(await Post(Url, Pdf, cliente: "adj-sin-extension", tipo: "otro"), HttpStatusCode.BadRequest, "invalid_tipo");

        body.TryGetProperty("estado", out _).Should().BeFalse();
        body.TryGetProperty("terminal", out _).Should().BeFalse();
    }

    [Fact]
    public async Task AC4_ElAdjuntoDelGestorGanaYSeConserva409AttachmentExists()
    {
        _factory.Envio.Reiniciar(Target("asignado") with
        {
            Vigentes = [new ExternalAttachmentExisting(Guid.CreateVersion7(), null, Convert.ToHexStringLower(SHA256.HashData(Pdf)), "fm-gestor")],
        });

        await ShouldBeProblem(await Post(Url, Pdf, cliente: "adj-gestor"), HttpStatusCode.Conflict, "attachment_exists");

        _factory.Envio.Retirados.Should().BeEmpty();
        _factory.Almacen.Borrados.Should().BeEmpty();
    }

    [Fact]
    public async Task AC5_OtroArchivoReemplazaYElMismoArchivoEsIdempotente200()
    {
        var anterior = new ExternalAttachmentExisting(Guid.CreateVersion7(), "flito", "no-es-el-mismo", "fm-anterior");
        _factory.Envio.Reiniciar(Target("asignado") with { Vigentes = [anterior] });

        var reemplazo = await Post(Url, Pdf, cliente: "adj-ac5");

        reemplazo.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(reemplazo)).GetProperty("reemplazoDe").GetGuid().Should().Be(anterior.Id);
        _factory.Almacen.Borrados.Should().Equal("fm-anterior");

        var vigente = new ExternalAttachmentExisting(Guid.CreateVersion7(), "flito", Convert.ToHexStringLower(SHA256.HashData(Pdf)), "fm-vigente");
        _factory.Envio.Reiniciar(Target("asignado") with { Vigentes = [vigente] });
        _factory.Almacen.Guardados.Clear();

        var repetido = await Post(Url, Pdf, cliente: "adj-ac5");

        repetido.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Json(repetido);
        body.GetProperty("adjuntoId").GetGuid().Should().Be(vigente.Id);
        body.GetProperty("reemplazoDe").ValueKind.Should().Be(JsonValueKind.Null);
        _factory.Envio.Escritos.Should().BeEmpty("el 200 idempotente no escribe nada");
        _factory.Almacen.Guardados.Should().BeEmpty();
    }

    [Fact]
    public async Task AC6_SinElPermisoDeEscrituraRecibe403InsufficientScopeAunConLosDeLectura()
    {
        var response = await Post(Url, Pdf, cliente: "adj-403", scopes: [ExternalScopes.TramitesRead, ExternalScopes.TramitesPiiRead]);

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, "insufficient_scope");
        _factory.Envio.Escritos.Should().BeEmpty();
    }

    [Fact]
    public async Task AC6_ElPermisoDeEscrituraSolo_NoDaAccesoALosEndpointsDeLectura()
    {
        using var request = Peticion("/api/v1/external/tramites/sync", "adj-solo-escritura", [ExternalScopes.AttachmentsWrite]);
        request.Method = HttpMethod.Get;

        await ShouldBeProblem(await _factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken),
            HttpStatusCode.Forbidden, "insufficient_scope");
    }

    [Fact]
    public async Task AC6_SinPase401InvalidToken()
    {
        using var content = new MultipartFormDataContent { { new StringContent("liquidacion_impuesto"), "tipo" } };

        var response = await _factory.CreateClient().PostAsync(Url, content, TestContext.Current.CancellationToken);

        await ShouldBeProblem(response, HttpStatusCode.Unauthorized, "invalid_token");
    }

    [Theory]
    [InlineData("inexistente")]
    [InlineData("id-mal-formado")]
    public async Task AC6_TramiteInexistenteFueraDeAlcanceOMalFormado404ProcedureNotFound(string caso)
    {
        var url = caso == "inexistente"
            ? $"/api/v1/external/tramites/{Guid.CreateVersion7()}/adjuntos"
            : "/api/v1/external/tramites/no-es-un-id/adjuntos";

        await ShouldBeProblem(await Post(url, Pdf, cliente: "adj-404"), HttpStatusCode.NotFound, "procedure_not_found");
        _factory.Almacen.Guardados.Should().BeEmpty();
    }

    [Fact]
    public async Task AC6_AlmacenamientoCaido503StorageUnavailableYNadaSeEscribe()
    {
        _factory.Almacen.Falla = new HttpRequestException("file-manager caído");

        await ShouldBeProblem(await Post(Url, Pdf, cliente: "adj-503"), HttpStatusCode.ServiceUnavailable, "storage_unavailable");

        _factory.Envio.Escritos.Should().BeEmpty();
    }

    [Fact]
    public async Task AC6_CadaLlamadaQuedaEnLaBitacoraComoEscrituraConDatosPersonalesSinElArchivo()
    {
        var response = await Post(Url, Pdf, cliente: "adj-bitacora");
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        await Post(Url, Pdf, cliente: "adj-bitacora", tipo: "otro");

        var filas = _factory.Bitacora.De("adj-bitacora");
        filas.Select(f => f.HttpStatus).Should().Equal(201, 400);
        filas.Should().OnlyContain(f => f.Endpoint == "tramites.adjunto-envio" && f.PiiUnmasked);
        filas[0].TenantIds.Should().Equal(Compania);
        filas[0].ItemsCount.Should().Be(1, "se escribió un adjunto");
        filas[1].ItemsCount.Should().BeNull("rechazada antes de resolver el trámite");
        filas[0].SyncVersionFrom.Should().BeNull();
        filas.Select(f => f.ToString()).Should().NotContain(t => t!.Contains("recibo.pdf", StringComparison.Ordinal) || t.Contains("%PDF", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AC6_LosRechazosDelPaseQuedanRegistradosComoElMismoEndpoint()
    {
        await Post(Url, Pdf, cliente: "adj-bitacora-403", scopes: [ExternalScopes.TramitesRead]);

        var fila = _factory.Bitacora.Unica("adj-bitacora-403");
        fila.HttpStatus.Should().Be(403);
        fila.Endpoint.Should().Be("tramites.adjunto-envio");
        fila.PiiUnmasked.Should().BeTrue();
    }

    [Fact]
    public async Task AC6_LaCuotaPorClienteTambienAplicaAlEnvio429()
    {
        await using var factory = _factory.ConCuota(1);
        var cliente = factory.CreateClient();
        HttpResponseMessage? ultima = null;
        for (var i = 0; i < 2; i++)
        {
            using var request = Peticion(factory, Url, "adj-cuota", [ExternalScopes.AttachmentsWrite]);
            request.Content = Multipart("liquidacion_impuesto", Pdf, "application/pdf", "recibo.pdf");
            ultima = await cliente.SendAsync(request, TestContext.Current.CancellationToken);
        }

        await ShouldBeProblem(ultima!, HttpStatusCode.TooManyRequests, "rate_limited");
        ultima!.Headers.RetryAfter.Should().NotBeNull();
    }

    private static ExternalAttachmentTarget Target(string estado, bool subsanacion = false) =>
        new(Tramite, Compania, estado, subsanacion, Guid.CreateVersion7(), null, false, []);

    private static async Task<JsonElement> ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await Json(response);
        body.GetProperty("code").GetString().Should().Be(code);
        body.GetProperty("title").GetString().Should().Be(code);
        body.GetProperty("status").GetInt32().Should().Be((int)status);
        return body;
    }

    private HttpRequestMessage Peticion(string url, string clientId, IReadOnlyList<string> scopes) =>
        Peticion(_factory, url, clientId, scopes);

    private static HttpRequestMessage Peticion(WebApplicationFactory<Program> factory, string url, string clientId, IReadOnlyList<string> scopes)
    {
        using var scope = factory.Services.CreateScope();
        var pase = scope.ServiceProvider.GetRequiredService<IExternalClientTokenIssuer>().Issue(clientId, scopes).Token;
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pase);
        return request;
    }

    private static MultipartFormDataContent Multipart(string? tipo, byte[]? archivo, string mime, string nombre)
    {
        var content = new MultipartFormDataContent();
        if (tipo is not null)
        {
            content.Add(new StringContent(tipo), "tipo");
        }

        if (archivo is not null)
        {
            var parte = new ByteArrayContent(archivo);
            parte.Headers.ContentType = new MediaTypeHeaderValue(mime);
            content.Add(parte, "file", nombre);
        }

        return content;
    }

    private async Task<HttpResponseMessage> Post(
        string url, byte[]? archivo, string cliente, string? tipo = "liquidacion_impuesto", string mime = "application/pdf",
        string nombre = "recibo.pdf", IReadOnlyList<string>? scopes = null)
    {
        using var request = Peticion(_factory, url, cliente, scopes ?? [ExternalScopes.AttachmentsWrite]);
        request.Content = Multipart(tipo, archivo, mime, nombre);
        return await _factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public EnvioEnMemoria Envio { get; } = new();

        public AlmacenEnMemoria Almacen { get; } = new();

        public MatrizFija Matriz { get; } = new();

        public ExternalAccessLogTests.BitacoraEnMemoria Bitacora { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IExternalAttachmentRepository>();
                services.AddSingleton<IExternalAttachmentRepository>(Envio);
                services.RemoveAll<IAttachmentStorage>();
                services.AddSingleton<IAttachmentStorage>(Almacen);
                services.RemoveAll<IResolvedChecklistMatrixProvider>();
                services.AddSingleton<IResolvedChecklistMatrixProvider>(Matriz);
                services.RemoveAll<IExternalAccessLogRepository>();
                services.AddSingleton<IExternalAccessLogRepository>(Bitacora);
            });

        public WebApplicationFactory<Program> ConCuota(int porMinuto) =>
            WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ExternalClients:RequestsPerClientPerMinute"] = porMinuto.ToString(System.Globalization.CultureInfo.InvariantCulture),
                })));
    }
}
