using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Flit.Admin.Domain.Integrations;
using Flit.Admin.Tests.Companies;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.ExternalSync;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Flit.Admin.Tests.Integrations;

/// <summary>
/// HU #13077 — <c>GET /api/v1/external/tramites/{id}/adjuntos/{adjuntoId}/url</c> contra el host real
/// (esquema externo, policies, serialización); el repositorio y el almacenamiento son dobles. El alcance
/// real de la búsqueda (solo facturas, radicado, no migrado, no eliminado) está en <c>Flit.Integration.Tests</c>.
/// </summary>
public sealed class ExternalAttachmentUrlEndpointTests : IClassFixture<ExternalAttachmentUrlEndpointTests.Factory>
{
    private static readonly Guid Tramite = Guid.CreateVersion7();
    private static readonly Guid Factura = Guid.CreateVersion7();
    private static readonly string Url = $"/api/v1/external/tramites/{Tramite}/adjuntos/{Factura}/url";

    private readonly Factory _factory;

    public ExternalAttachmentUrlEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Repo.Facturas.Clear();
        _factory.Repo.Facturas[(Tramite, Factura)] = new ProcedureSyncInvoiceFile("fm-0001", "factura.pdf", "application/pdf");
        _factory.Storage.Firmas.Clear();
        _factory.Storage.Firmas["fm-0001"] = ("https://almacen.ejemplo.test/firmada?sig=x",
            new DateTimeOffset(2026, 9, 21, 15, 30, 0, TimeSpan.Zero));
        _factory.Storage.Falla = null;
    }

    [Fact]
    public async Task AC1_FacturaDelTramiteDevuelveUrlFirmadaYSusDatos()
    {
        var response = await Get(Url, ExternalScopes.Todos);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await Json(response);
        body.GetProperty("url").GetString().Should().Be("https://almacen.ejemplo.test/firmada?sig=x");
        body.GetProperty("expiraEn").GetString().Should().Be("2026-09-21T10:30:00-05:00", "hora de Colombia, como el resto del contrato");
        body.GetProperty("nombreArchivo").GetString().Should().Be("factura.pdf");
        body.GetProperty("contentType").GetString().Should().Be("application/pdf");
        body.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("url", "expiraEn", "nombreArchivo", "contentType");
    }

    [Fact]
    public async Task AC1_BastaElPermisoDeLecturaSinElDeDatosPersonales()
    {
        var response = await Get(Url, [ExternalScopes.TramitesRead]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("otro-adjunto")]
    [InlineData("otro-tramite")]
    [InlineData("id-mal-formado")]
    public async Task AC2_AdjuntoInexistenteAjenoOMalFormado404AttachmentNotFound(string caso)
    {
        var url = caso switch
        {
            "otro-adjunto" => $"/api/v1/external/tramites/{Tramite}/adjuntos/{Guid.CreateVersion7()}/url",
            "otro-tramite" => $"/api/v1/external/tramites/{Guid.CreateVersion7()}/adjuntos/{Factura}/url",
            _ => $"/api/v1/external/tramites/no-es-un-id/adjuntos/{Factura}/url",
        };

        await ShouldBeProblem(await Get(url, ExternalScopes.Todos), HttpStatusCode.NotFound, "attachment_not_found");
    }

    [Fact]
    public async Task AC2_ArchivoQueElAlmacenamientoYaNoTiene404()
    {
        _factory.Storage.Firmas.Clear();

        await ShouldBeProblem(await Get(Url, ExternalScopes.Todos), HttpStatusCode.NotFound, "attachment_not_found");
    }

    [Fact]
    public async Task AlmacenamientoCaido503StorageUnavailable()
    {
        _factory.Storage.Falla = new HttpRequestException("file-manager caído");

        await ShouldBeProblem(await Get(Url, ExternalScopes.Todos), HttpStatusCode.ServiceUnavailable, "storage_unavailable");
    }

    [Fact]
    public async Task SinPase401YSinPermisoDeLectura403()
    {
        var sinPase = await _factory.CreateClient().GetAsync(Url, TestContext.Current.CancellationToken);

        await ShouldBeProblem(sinPase, HttpStatusCode.Unauthorized, "invalid_token");
        await ShouldBeProblem(await Get(Url, [ExternalScopes.TramitesPiiRead]), HttpStatusCode.Forbidden, "insufficient_scope");
    }

    private static async Task ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await Json(response)).GetProperty("code").GetString().Should().Be(code);
    }

    private async Task<HttpResponseMessage> Get(string url, IReadOnlyList<string> scopes)
    {
        using var scope = _factory.Services.CreateScope();
        var pase = scope.ServiceProvider.GetRequiredService<IExternalClientTokenIssuer>().Issue("flito-test", scopes).Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pase);
        return await _factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public FakeInvoiceRepository Repo { get; } = new();

        public FakeStorage Storage { get; } = new();

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProcedureSyncReadRepository>();
                services.AddSingleton<IProcedureSyncReadRepository>(Repo);
                services.RemoveAll<IAttachmentStorage>();
                services.AddSingleton<IAttachmentStorage>(Storage);
            });
    }

    public sealed class FakeInvoiceRepository : IProcedureSyncReadRepository
    {
        public Dictionary<(Guid, Guid), ProcedureSyncInvoiceFile> Facturas { get; } = [];

        public Task<ProcedureSyncInvoiceFile?> FindInvoiceAsync(Guid procedureId, Guid attachmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Facturas.GetValueOrDefault((procedureId, attachmentId)));

        public Task<IReadOnlyList<ProcedureSyncEntry>> ReadItemsAsync(ProcedureSyncPageRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ProcedureSyncChange>> ReadChangesAsync(ProcedureSyncPageRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    public sealed class FakeStorage : IAttachmentStorage
    {
        public Dictionary<string, (string Url, DateTimeOffset ExpiresAt)> Firmas { get; } = [];

        public Exception? Falla { get; set; }

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(string storagePath, CancellationToken ct = default) =>
            Falla is not null
                ? Task.FromException<(string Url, DateTimeOffset ExpiresAt)?>(Falla)
                : Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(Firmas.TryGetValue(storagePath, out var f) ? f : null);

        public Task<StoredFile> SaveAsync(Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PresignedUpload> CreatePresignedUploadAsync(Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
