using System.Net;
using System.Text;
using System.Text.Json;
using Flit.DrFlit.Application.SupportCases;
using Flit.Infrastructure.DrFlit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.DrFlit;

/// <summary>
/// HU #12923 — cliente REST de Azure DevOps que radica los casos de DR. FLIT como Bug.
/// Uso de ejemplo:
/// <code>
/// var client = new AzureDevOpsSupportCaseClient(http, Options.Create(ado), Options.Create(mapping), logger);
/// var r = await client.CreateBugAsync(ticket, attachments, ct); // r.Created, r.WorkItemId, r.AttachmentsFailed
/// </code>
/// </summary>
[Collection(DrFlitEnvVarsTestGroup.Name)]
public sealed class AzureDevOpsSupportCaseClientTests
{
    private static readonly DrFlitSupportTicket Ticket = new(
        Title: "No puedo subir la factura",
        RequesterName: "Usuario <b>Prueba</b>",
        RequesterEmail: "usuario.prueba@example.test",
        RequesterPhone: "3000000000",
        Company: "Empresa Demo S.A.S",
        Detail: "Al subir la factura sale error.\nSegunda línea <script>alert(1)</script>",
        ExpectedResult: "Que la factura quede cargada",
        Frequency: DrFlitCaseFrequency.Siempre,
        Priority: DrFlitCasePriority.Alta,
        Environment: DrFlitDeployEnvironment.QA,
        AffectedModule: "matricula",
        ReportedAt: new DateTimeOffset(2026, 9, 26, 2, 0, 0, TimeSpan.Zero)); // 25/09 21:00 en Bogotá

    private static AzureDevOpsSupportCaseClient Client(
        Handler handler, DrFlitFieldMappingOptions? mapping = null, AzureDevOpsOptions? ado = null) =>
        new(
            new HttpClient(handler),
            Options.Create(ado ?? new AzureDevOpsOptions { Pat = "pat-de-prueba", TimeoutSeconds = 1 }),
            Options.Create(mapping ?? new DrFlitFieldMappingOptions()),
            NullLogger<AzureDevOpsSupportCaseClient>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Created(int id = 13001) => Json(HttpStatusCode.OK, $$"""{"id":{{id}},"rev":1}""");

    private static DrFlitTicketAttachment Attachment(string name, byte[]? bytes = null) =>
        new(name, "image/png", _ => Task.FromResult<Stream?>(bytes is null ? null : new MemoryStream(bytes)));

    /// <summary>Operaciones del JSON Patch enviado al crear el Bug, por path.</summary>
    private static Dictionary<string, JsonElement> Fields(string patchJson)
    {
        using var doc = JsonDocument.Parse(patchJson);
        return doc.RootElement.EnumerateArray()
            .Where(op => op.GetProperty("path").GetString()!.StartsWith("/fields/", StringComparison.Ordinal))
            .ToDictionary(op => op.GetProperty("path").GetString()!["/fields/".Length..], op => op.GetProperty("value").Clone());
    }

    // ── AC1 — creación con mapeo configurado ────────────────────────────────────────────

    [Fact]
    public async Task AC1_MapeaCamposConLosDefaultsDelProyectoDeSoporte()
    {
        var handler = new Handler((req, body) => Created());

        var result = await Client(handler).CreateBugAsync(Ticket, [], TestContext.Current.CancellationToken);

        result.Should().Be(new DrFlitBugCreationResult(
            true, 13001, "https://dev.azure.com/FlitDevOps/FLIT%20-%20SOPORTE/_workitems/edit/13001", 0, null));
        var fields = Fields(handler.Bodies.Single());
        fields["System.Title"].GetString().Should().Be("[ DR. FLIT ] No puedo subir la factura");
        fields["Custom.Primacy"].GetString().Should().Be("1");
        fields["Microsoft.VSTS.Common.Severity"].GetString().Should().Be("2 - High");
        fields["Custom.Incidence"].GetString().Should().Be("3+");
        fields["Custom.Environment"].GetString().Should().Be("QA");
        fields["Custom.AffectedModule"].GetString().Should().Be("Matricula");
        fields["Custom.TypeBug"].GetString().Should().Be("Sin Definir");
        fields["System.AssignedTo"].GetString().Should().Be(
            "Soporte@flitsas.com", "el Bug entra directo a la cola de la cuenta de soporte");
    }

    [Fact]
    public async Task AC1_ElMapeoSaleDeLaConfiguracion_NoDeConstantes()
    {
        var mapping = new DrFlitFieldMappingOptions
        {
            Primacy = new() { ["Alta"] = "4" },
            Severity = new() { ["Alta"] = "1 - Critical" },
            Incidence = new() { ["siempre"] = "2" },
            Environment = new() { ["QA"] = "PDN" },
            TypeBug = "Incidente",
        };
        var handler = new Handler((_, _) => Created());

        await Client(handler, mapping, new AzureDevOpsOptions { Pat = "p", TitlePrefix = "[ BOT ]", AssignedTo = "otra.cola@flitsas.com" })
            .CreateBugAsync(Ticket, [], TestContext.Current.CancellationToken);

        var fields = Fields(handler.Bodies.Single());
        fields["System.Title"].GetString().Should().StartWith("[ BOT ] ");
        fields["Custom.Primacy"].GetString().Should().Be("4");
        fields["Microsoft.VSTS.Common.Severity"].GetString().Should().Be("1 - Critical");
        fields["Custom.Incidence"].GetString().Should().Be("2");
        fields["Custom.Environment"].GetString().Should().Be("PDN");
        fields["Custom.TypeBug"].GetString().Should().Be("Incidente");
        fields["System.AssignedTo"].GetString().Should().Be("otra.cola@flitsas.com");
    }

    [Fact]
    public async Task AC1_AssignedToVacio_CreaElBugSinAsignar()
    {
        var handler = new Handler((_, _) => Created());

        await Client(handler, ado: new AzureDevOpsOptions { Pat = "p", AssignedTo = " " })
            .CreateBugAsync(Ticket, [], TestContext.Current.CancellationToken);

        Fields(handler.Bodies.Single()).Should().NotContainKey("System.AssignedTo", "vacío conserva el comportamiento de triage sin asignar");
    }

    [Fact]
    public async Task AC1_ClaveSinMapeo_NoEnviaElCampo()
    {
        var mapping = new DrFlitFieldMappingOptions { Primacy = new() { ["Media"] = "2" } };
        var handler = new Handler((_, _) => Created());

        await Client(handler, mapping).CreateBugAsync(Ticket, [], TestContext.Current.CancellationToken);

        Fields(handler.Bodies.Single()).Should().NotContainKey("Custom.Primacy");
    }

    [Fact]
    public void AC1_ReproStepsConLasEtiquetasDelFormularioWebYSinHtmlDelUsuario()
    {
        var html = AzureDevOpsSupportCaseClient.BuildReproStepsHtml(Ticket, ["captura.png"]);

        var labels = System.Text.RegularExpressions.Regex.Matches(html, "<li>([^:<]+):").Select(m => m.Groups[1].Value);
        labels.Should().Equal(
            "Nombre", "email del usuario", "compañía", "fecha", "Detalle del Error", "Resultado Esperado",
            "Adjuntos", "Frecuencia del Error", "Ambiente", "Teléfono", "Título", "Prioridad", "Correo", "Origen");
        html.Should().StartWith("<html><head></head><body><ol>").And.EndWith("</ol></body></html>");
        html.Should().Contain("<li>fecha: 25/09/2026</li>", "la fecha va en hora Colombia");
        html.Should().Contain("<li>Frecuencia del Error: Siempre</li>");
        html.Should().Contain("<li>Ambiente: QA</li>");
        html.Should().Contain("<li>Prioridad: Alta</li>");
        html.Should().Contain("<li>Adjuntos: captura.png</li>");
        html.Should().Contain("Al subir la factura sale error.<br>Segunda línea &lt;script&gt;alert(1)&lt;/script&gt;");
        html.Should().Contain("Usuario &lt;b&gt;Prueba&lt;/b&gt;");
        html.Should().NotContain("<script>").And.NotContain("<b>Prueba");
        html.Should().Contain("<a href=\"mailto:usuario.prueba@example.test\">");
    }

    [Fact]
    public async Task AC1_DescriptionLlevaElMismoContenidoQueReproSteps()
    {
        var handler = new Handler((_, _) => Created());

        await Client(handler).CreateBugAsync(Ticket, [], TestContext.Current.CancellationToken);

        var fields = Fields(handler.Bodies.Single());
        fields["System.Description"].GetString().Should().Be(fields["Microsoft.VSTS.TCM.ReproSteps"].GetString());
    }

    [Fact]
    public async Task AC1_AutenticaConElPatEnBasicYUsaJsonPatch()
    {
        var handler = new Handler((_, _) => Created());

        await Client(handler).CreateBugAsync(Ticket, [], TestContext.Current.CancellationToken);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Headers.Authorization!.Scheme.Should().Be("Basic");
        Encoding.ASCII.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)).Should().Be(":pat-de-prueba");
        handler.ContentTypes.Single().Should().Be("application/json-patch+json");
    }

    // ── AC2 — proyecto con espacios en el nombre ────────────────────────────────────────

    [Fact]
    public async Task AC2_ProyectoConEspacios_QuedaCodificadoEnLaRuta()
    {
        var handler = new Handler((_, _) => Created());

        await Client(handler).CreateBugAsync(Ticket, [Attachment("mi captura.png", [1, 2])], TestContext.Current.CancellationToken);

        handler.Requests[0].RequestUri!.AbsoluteUri.Should().Be(
            "https://dev.azure.com/FlitDevOps/FLIT%20-%20SOPORTE/_apis/wit/attachments?fileName=mi%20captura.png&api-version=7.1");
        handler.Requests[1].RequestUri!.AbsoluteUri.Should().Be(
            "https://dev.azure.com/FlitDevOps/FLIT%20-%20SOPORTE/_apis/wit/workitems/$Bug?api-version=7.1");
    }

    // ── AC3 — adjuntos no bloqueantes ───────────────────────────────────────────────────

    [Fact]
    public async Task AC3_AdjuntoFallido_SeExcluyeYSeCuenta_ElBugSeCreaConLosDemas()
    {
        var handler = new Handler((req, _) =>
        {
            var query = req.RequestUri!.Query;
            if (query.Contains("fileName=falla.png", StringComparison.Ordinal))
                return Json(HttpStatusCode.BadRequest, "{}");
            if (query.Contains("fileName=", StringComparison.Ordinal))
            {
                var name = Uri.UnescapeDataString(query.Split("fileName=")[1].Split('&')[0]);
                return Json(HttpStatusCode.Created, $$"""{"id":"x","url":"https://ado.test/attachments/{{name}}"}""");
            }
            return Created();
        });

        var result = await Client(handler).CreateBugAsync(
            Ticket,
            [Attachment("uno.png", [1]), Attachment("falla.png", [2]), Attachment("sin-contenido.png"), Attachment("dos.pdf", [3])],
            TestContext.Current.CancellationToken);

        result.Created.Should().BeTrue();
        result.AttachmentsFailed.Should().Be(2);
        using var doc = JsonDocument.Parse(handler.Bodies.Last());
        var relations = doc.RootElement.EnumerateArray()
            .Where(op => op.GetProperty("path").GetString() == "/relations/-")
            .Select(op => (Rel: op.GetProperty("value").GetProperty("rel").GetString(), Url: op.GetProperty("value").GetProperty("url").GetString()))
            .ToList();
        relations.Should().Equal(
            ("AttachedFile", "https://ado.test/attachments/uno.png"),
            ("AttachedFile", "https://ado.test/attachments/dos.pdf"));
        Fields(handler.Bodies.Last())["Microsoft.VSTS.TCM.ReproSteps"].GetString().Should().Contain("<li>Adjuntos: uno.png, dos.pdf</li>");
    }

    // ── AC4 — módulo afectado con default configurado ───────────────────────────────────

    [Theory]
    [InlineData(null, "Otros Tramites")]
    [InlineData("", "Otros Tramites")]
    [InlineData("modulo-inventado", "Otros Tramites")]
    [InlineData("  validación de identidad ", "Validación de Identidad")]
    [InlineData("Traspasos", "Traspasos")]
    public void AC4_ModuloFueraDelAllowList_UsaElDefaultConfigurado(string? requested, string expected)
    {
        Client(new Handler((_, _) => Created())).ResolveAffectedModule(requested).Should().Be(expected);
    }

    [Fact]
    public void AC4_DefaultYAllowListSalenDeLaConfiguracion()
    {
        var mapping = new DrFlitFieldMappingOptions { AffectedModules = ["Solo Este"], DefaultAffectedModule = "Solo Este" };
        var client = Client(new Handler((_, _) => Created()), mapping);

        client.ResolveAffectedModule("Traspasos").Should().Be("Solo Este");
    }

    // ── Resiliencia ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Error5xx_ReintentaUnaVezYRecupera()
    {
        var calls = 0;
        var handler = new Handler((_, _) => ++calls == 1 ? Json(HttpStatusCode.ServiceUnavailable, "{}") : Created(77));

        var result = await Client(handler).CreateBugAsync(Ticket, [], TestContext.Current.CancellationToken);

        result.WorkItemId.Should().Be(77);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Error5xxPersistente_FallaConCodigoSinPii()
    {
        var calls = 0;
        var handler = new Handler((_, _) => { calls++; return Json(HttpStatusCode.BadGateway, "{}"); });

        var result = await Client(handler).CreateBugAsync(Ticket, [], TestContext.Current.CancellationToken);

        result.Should().Be(DrFlitBugCreationResult.Failed("http_502"));
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Error4xx_NoReintenta()
    {
        var calls = 0;
        var handler = new Handler((_, _) => { calls++; return Json(HttpStatusCode.BadRequest, "{}"); });

        var result = await Client(handler).CreateBugAsync(Ticket, [], TestContext.Current.CancellationToken);

        result.ErrorCode.Should().Be("http_400");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task TimeoutEnAmbosIntentos_FallaConTimeout()
    {
        var handler = new Handler(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Created();
        });

        var result = await Client(handler).CreateBugAsync(Ticket, [], TestContext.Current.CancellationToken);

        result.Should().Be(DrFlitBugCreationResult.Failed("timeout"));
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task SinPat_NoLlamaYFallaNotConfigured()
    {
        var handler = new Handler((_, _) => Created());

        var result = await Client(handler, ado: new AzureDevOpsOptions { Pat = "" })
            .CreateBugAsync(Ticket, [Attachment("a.png", [1])], TestContext.Current.CancellationToken);

        result.Should().Be(DrFlitBugCreationResult.Failed("not_configured"));
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RespuestaSinId_FallaInvalidResponse()
    {
        var handler = new Handler((_, _) => Json(HttpStatusCode.OK, "{}"));

        (await Client(handler).CreateBugAsync(Ticket, [], TestContext.Current.CancellationToken))
            .ErrorCode.Should().Be("invalid_response");
    }

    // ── Configuración ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Configuracion_EnvTienePrioridadYLaListaDeModulosReemplazaLaDefault()
    {
        const string patVar = "DR_FLIT_ADO_PAT";
        try
        {
            Environment.SetEnvironmentVariable(patVar, "pat-del-env");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPostgresInfrastructure(
                "Host=localhost;Database=flit_di_validation_only;Username=flit;Password=flit",
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DrFlit:AzureDevOps:Pat"] = "pat-de-appsettings",
                    ["DrFlit:AzureDevOps:Project"] = "OTRO PROYECTO",
                    ["DrFlit:SupportCase:FieldMapping:Primacy:Alta"] = "4",
                    ["DrFlit:SupportCase:FieldMapping:AffectedModules:0"] = "Login",
                    ["DrFlit:SupportCase:FieldMapping:DefaultAffectedModule"] = "Login",
                }).Build(),
                new FakeEnvironment());
            using var sp = services.BuildServiceProvider();

            var ado = sp.GetRequiredService<IOptions<AzureDevOpsOptions>>().Value;
            var mapping = sp.GetRequiredService<IOptions<DrFlitFieldMappingOptions>>().Value;

            ado.Pat.Should().Be("pat-del-env");
            ado.Project.Should().Be("OTRO PROYECTO");
            ado.OrganizationUrl.Should().Be("https://dev.azure.com/FlitDevOps");
            mapping.Primacy.Should().Contain(new KeyValuePair<string, string>("Alta", "4")).And.ContainKey("Media");
            mapping.AffectedModules.Should().Equal("Login");
            mapping.DefaultAffectedModule.Should().Be("Login");
        }
        finally
        {
            Environment.SetEnvironmentVariable(patVar, null);
        }
    }

    internal sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, CancellationToken, Task<HttpResponseMessage>> _responder;

        public Handler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder)
            : this((req, body, _) => Task.FromResult(responder(req, body)))
        {
        }

        public Handler(Func<HttpRequestMessage, string?, CancellationToken, Task<HttpResponseMessage>> responder) =>
            _responder = responder;

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        public List<string?> ContentTypes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.RequestUri!.AbsolutePath.EndsWith("$Bug", StringComparison.Ordinal))
            {
                Bodies.Add(body!);
                ContentTypes.Add(request.Content!.Headers.ContentType!.MediaType);
            }
            return await _responder(request, body, cancellationToken);
        }
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Flit.Infrastructure.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
