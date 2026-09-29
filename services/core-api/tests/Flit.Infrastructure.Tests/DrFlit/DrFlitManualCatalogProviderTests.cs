using System.Net;
using System.Text;
using System.Text.Json;
using Flit.DrFlit.Application.Chat;
using Flit.Infrastructure.DrFlit;
using Flit.Infrastructure.Ocr;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.DrFlit;

/// <summary>
/// HU #12921 — proveedor del manual de DR. FLIT sobre el artefacto generado (HU #12920).
/// Uso de ejemplo:
/// <code>
/// var provider = new DrFlitManualCatalogProvider(hostEnvironment, logger);
/// var catalog = provider.GetCatalog(); // null ⇒ el chat degrada; si no, BySlug + SystemPrompt listos
/// </code>
/// </summary>
public sealed class DrFlitManualCatalogProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "drflit-catalog-" + Guid.NewGuid().ToString("N"));

    public DrFlitManualCatalogProviderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>El artefacto real commiteado, ubicado subiendo desde la carpeta de salida del test.</summary>
    private static string RealArtifactPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Flit.Api", DrFlitManualCatalogProvider.DefaultRelativePath);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("No se encontró src/Flit.Api/Content/dr-flit/manual-catalog.generated.json");
    }

    private static DrFlitManualCatalogProvider Provider(string path) =>
        new(path, NullLogger<DrFlitManualCatalogProvider>.Instance);

    private string Write(string json)
    {
        var path = Path.Combine(_dir, "catalog.json");
        File.WriteAllText(path, json, Encoding.UTF8);
        return path;
    }

    private const string OneArticle = """
        {"schemaVersion":1,"articleCount":1,"articles":[{
          "slug":"1-gestor/2-crear-tramite","title":"Crear un trámite","href":"/manual/1-gestor/2-crear-tramite",
          "audience":"Gestor","sectionId":"gestor","summary":"Cómo iniciar un trámite.","primarySource":false,
          "blocks":[{"id":"b1","title":"Pasos","paragraphs":["Abre el wizard."],"bullets":["Elige el tipo"],
                     "callouts":[{"variant":"tip","title":"Ojo","text":"Guarda el borrador."}]}],
          "sources":[{"title":"Resolución 20233040017145","href":"/legal/res.pdf","kind":"pdf","ref":"DO 52386"}]}]}
        """;

    // ── AC1 — carga el artefacto y expone slug/título/href ─────────────────────────────

    [Fact]
    public void AC1_ArtefactoReal_CargaTodosLosArticulos()
    {
        var path = RealArtifactPath();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var expectedCount = doc.RootElement.GetProperty("articleCount").GetInt32();

        var catalog = Provider(path).GetCatalog();

        catalog.Should().NotBeNull();
        catalog!.Articles.Should().HaveCount(expectedCount);
        catalog.BySlug.Should().HaveCount(expectedCount, "los slugs del manual son únicos");
        catalog.BySlug.Should().ContainKey("1-gestor/2-crear-tramite");
        catalog.Articles.Should().AllSatisfy(a =>
        {
            a.Href.Should().Be($"/manual/{a.Slug}");
            a.Text.Should().NotBeNullOrWhiteSpace();
        });
    }

    [Fact]
    public void AC1_AplanaResumenBloquesVinetasAvisosYFuentes()
    {
        var article = Provider(Write(OneArticle)).GetCatalog()!.BySlug["1-gestor/2-crear-tramite"];

        article.Title.Should().Be("Crear un trámite");
        article.Href.Should().Be("/manual/1-gestor/2-crear-tramite");
        article.SourceHref.Should().Be("/legal/res.pdf");
        article.Text.Should().Be(
            "aplica para: Gestor\n" +
            "Cómo iniciar un trámite.\n" +
            "\n## Pasos\n" +
            "Abre el wizard.\n" +
            "- Elige el tipo\n" +
            "Nota (Ojo): Guarda el borrador.\n" +
            "\nfuentes: Resolución 20233040017145 (DO 52386)");
    }

    [Fact]
    public void AC1_SeLeeUnaSolaVez()
    {
        var path = Write(OneArticle);
        var provider = Provider(path);

        var first = provider.GetCatalog();
        File.Delete(path);

        provider.GetCatalog().Should().BeSameAs(first);
    }

    // ── AC2 — sin artefacto válido: degradado, sin excepción ───────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("no es json")]
    [InlineData("""{"schemaVersion":2,"articles":[{"slug":"a","title":"A"}]}""")]
    [InlineData("""{"articles":[{"slug":"a","title":"A"}]}""")]
    [InlineData("""{"schemaVersion":1,"articles":[]}""")]
    [InlineData("""{"schemaVersion":1,"articles":[{"slug":"","title":"Sin slug"}]}""")]
    public void AC2_ArtefactoAusenteOInvalido_DevuelveNullSinLanzar(string? content)
    {
        var path = content is null ? Path.Combine(_dir, "no-existe.json") : Write(content);

        var act = () => Provider(path).GetCatalog();

        act.Should().NotThrow().Which.Should().BeNull();
    }

    [Fact]
    public void AC2_ContentRootSinArtefacto_ElProveedorDelHostDevuelveNull()
    {
        var provider = new DrFlitManualCatalogProvider(
            new FakeEnvironment { ContentRootPath = _dir },
            NullLogger<DrFlitManualCatalogProvider>.Instance);

        provider.GetCatalog().Should().BeNull();
    }

    // ── AC3 — el manual es el primer bloque del system, cacheable ─────────────────────

    [Fact]
    public async Task AC3_ManualPrimeroYCacheable_InstruccionesDespues()
    {
        var catalog = Provider(RealArtifactPath()).GetCatalog()!;
        string? sent = null;
        var handler = new CapturingHandler(async (req, ct) =>
        {
            sent = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"content":[{"type":"text","text":"{}"}]}""", Encoding.UTF8, "application/json"),
            };
        });
        var options = Options.Create(new AnthropicOptions { ApiKey = "sk-ant-test" });
        var model = new AnthropicDrFlitChatModel(
            new AnthropicMessagesClient(
                new HttpClient(handler) { BaseAddress = new Uri("https://anthropic.test") },
                options,
                NullLogger<AnthropicMessagesClient>.Instance),
            options);

        await model.CompleteAsync(catalog.SystemPrompt, [new DrFlitTurn(DrFlitTurnRole.User, "hola")], TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(sent!);
        var system = doc.RootElement.GetProperty("system").EnumerateArray().ToList();
        system.Should().HaveCount(2);
        system[0].GetProperty("text").GetString().Should()
            .StartWith($"MANUAL DE FLIT ({catalog.Articles.Count} artículos).")
            .And.Contain("slug: 1-gestor/2-crear-tramite");
        system[1].GetProperty("text").GetString().Should().Be(DrFlitPromptBuilder.Instructions);
        system.Should().AllSatisfy(b =>
            b.GetProperty("cache_control").GetProperty("type").GetString().Should().Be("ephemeral"));
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }

    private sealed class FakeEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Flit.Infrastructure.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
