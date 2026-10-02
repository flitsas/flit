using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Admin.Domain.Integrations;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.ExternalSync;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Flit.Admin.Tests.Integrations;

/// <summary>
/// HU #13082 (Feature #13066, Épica #12737) — el contrato publicado <c>contracts/openapi/external-api.v1.json</c>
/// describe lo que la API realmente responde. Las respuestas del host real (dobles solo en datos) se validan contra
/// los esquemas del contrato: un campo que falte, sobre o cambie de tipo rompe esta prueba y con ella el CI (AC2).
/// También se validan los ejemplos del contrato, la marca <c>x-pii</c> frente al enmascarado real y las rutas.
/// El lint del documento (estructura OpenAPI) lo hace Redocly en <c>.github/workflows/contracts.yml</c>.
/// </summary>
public sealed class ExternalApiContractTests : IClassFixture<ExternalApiContractTests.Factory>
{
    private static readonly Lazy<JsonDocument> Contrato = new(() => JsonDocument.Parse(File.ReadAllText(RutaContrato())));
    private static readonly Guid Tramite = new("0192b7c4-5e6a-7d10-9f21-3a4b5c6d7e8f");
    private static readonly Guid Factura = new("0192b7c4-9a1b-7c2d-8e3f-4a5b6c7d8e9f");

    private readonly Factory _factory;

    public ExternalApiContractTests(Factory factory) => _factory = factory;

    private static JsonElement Schemas => Contrato.Value.RootElement.GetProperty("components").GetProperty("schemas");

    [Fact]
    public async Task AC1_LaPaginaRealCumpleElEsquemaConYSinDatosPersonales()
    {
        _factory.Feed.Entradas = [Entrada(ItemCompleto()), Entrada(Tombstone()), Entrada(ItemConNulos())];

        foreach (var scopes in new[] { ExternalScopes.Todos, [ExternalScopes.TramitesRead] })
        {
            var response = await Get("/api/v1/external/tramites/sync", scopes);
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
            Validar(await Json(response), "SyncPage");
        }
    }

    [Fact]
    public async Task AC1_LaPaginaVaciaCumpleElEsquema()
    {
        _factory.Feed.Entradas = [];

        Validar(await Json(await Get("/api/v1/external/tramites/sync", ExternalScopes.Todos)), "SyncPage");
    }

    [Fact]
    public async Task AC1_LaUrlDeLaFacturaCumpleElEsquema()
    {
        var response = await Get($"/api/v1/external/tramites/{Tramite}/adjuntos/{Factura}/url", ExternalScopes.Todos);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        Validar(await Json(response), "AdjuntoUrl");
    }

    [Fact]
    public async Task AC1_ElPaseCumpleElEsquema()
    {
        var clientId = _factory.Clientes.Alta(_factory.Services, "secreto-de-contrato");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/external/auth/token")
        {
            Content = JsonContent.Create(new { clientId, clientSecret = "secreto-de-contrato" }),
        };
        request.Headers.Add("X-Forwarded-For", "192.0.2.82");

        var response = await _factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        Validar(await Json(response), "TokenResponse");
    }

    [Theory]
    [InlineData("/api/v1/external/tramites/sync?cursor=basura", 400)]
    [InlineData("/api/v1/external/tramites/sync?since=ayer", 400)]
    [InlineData("/api/v1/external/tramites/0192b7c4-0000-7000-8000-000000000000/adjuntos/0192b7c4-0000-7000-8000-000000000001/url", 404)]
    public async Task AC1_LosErroresCumplenElEsquemaProblemYSuCodigoEstaDocumentado(string url, int status)
    {
        var response = await Get(url, ExternalScopes.Todos);

        ((int)response.StatusCode).Should().Be(status);
        var body = await Json(response);
        Validar(body, "Problem");
        CodigosDocumentados(url.Contains("/url", StringComparison.Ordinal) ? "/api/v1/external/tramites/{id}/adjuntos/{adjuntoId}/url" : "/api/v1/external/tramites/sync", "get", status)
            .Should().Contain(body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC1_SinPaseYSinPermisoCumplenElEsquemaProblem()
    {
        Validar(await Json(await _factory.CreateClient().GetAsync("/api/v1/external/tramites/sync", TestContext.Current.CancellationToken)), "Problem");
        Validar(await Json(await Get("/api/v1/external/tramites/sync", [ExternalScopes.TramitesPiiRead])), "Problem");
    }

    [Fact]
    public void AC1_LosEjemplosDelContratoCumplenSusEsquemasYSonFicticios()
    {
        var ejemplos = 0;
        foreach (var (esquema, ejemplo) in Ejemplos(Contrato.Value.RootElement.GetProperty("paths")))
        {
            Validar(ejemplo, esquema);
            ejemplos++;
        }

        ejemplos.Should().BeGreaterThanOrEqualTo(10);
        var texto = Contrato.Value.RootElement.GetRawText();
        foreach (System.Text.RegularExpressions.Match correo in System.Text.RegularExpressions.Regex.Matches(texto, @"[\w.+-]+@[\w-]+(\.[\w-]+)+"))
        {
            correo.Value.Should().EndWith(".test", "los ejemplos usan dominios reservados, nunca correos reales");
        }
    }

    [Fact]
    public void AC1_LosCamposMarcadosComoPersonalesSonLosQueSeEnmascaran()
    {
        var marcados = Schemas.GetProperty("Comprador").GetProperty("properties").EnumerateObject()
            .Where(p => p.Value.TryGetProperty("x-pii", out _))
            .Select(p => p.Name)
            .ToHashSet();

        var original = JsonSerializer.SerializeToElement(ItemCompleto().Compradores[0], JsonSerializerOptions.Web);
        var enmascarado = JsonSerializer.SerializeToElement(ProcedureSyncPiiMasker.Mask(ItemCompleto()).Compradores[0], JsonSerializerOptions.Web);
        var cambian = original.EnumerateObject()
            .Where(p => p.Value.GetRawText() != enmascarado.GetProperty(p.Name).GetRawText())
            .Select(p => p.Name)
            .ToHashSet();

        marcados.Should().BeEquivalentTo(cambian);
    }

    [Fact]
    public void AC1_ElContratoTieneEsquemaDeSeguridadPropioYCubreTodasLasRutasExternas()
    {
        var raiz = Contrato.Value.RootElement;
        var esquema = raiz.GetProperty("components").GetProperty("securitySchemes").GetProperty("externalClient");
        esquema.GetProperty("type").GetString().Should().Be("http");
        esquema.GetProperty("scheme").GetString().Should().Be("bearer");
        raiz.GetProperty("security")[0].TryGetProperty("externalClient", out _).Should().BeTrue();

        var enContrato = Operaciones(raiz).Where(o => !o.Anunciada).Select(o => o.Ruta).ToHashSet();
        var enLaApi = RutasExternasDeLaApi();

        enContrato.Should().BeEquivalentTo(enLaApi, "cada ruta externa está documentada y el contrato no promete rutas que no existen");
    }

    /// <summary>
    /// HU #13262 (Feature #13261, Épica #12741) — una operación con <c>x-estado: anunciada</c> se publica antes de
    /// existir para que el consumidor implemente en paralelo. Mientras la API no la exponga, la marca es obligatoria;
    /// en cuanto la exponga, esta prueba falla hasta quitar la marca y la ruta pasa a la verificación de arriba.
    /// </summary>
    [Fact]
    public void AC4_LasOperacionesAnunciadasNoExistenTodaviaEnLaApi()
    {
        var anunciadas = Operaciones(Contrato.Value.RootElement).Where(o => o.Anunciada).Select(o => o.Ruta).ToList();

        anunciadas.Should().Contain("POST /api/v1/external/tramites/{id}/adjuntos");
        RutasExternasDeLaApi().Should().NotIntersectWith(anunciadas, "al implementarla se quita x-estado: anunciada del contrato");
    }

    /// <summary>
    /// HU #13262 — el envío de adjuntos publica lo acordado con Flito: cuerpo multipart con la lista cerrada de tipos,
    /// el mismo cuerpo en 201 y 200, cada código de error en su estado y el 409 de estado con <c>estado</c> y
    /// <c>terminal</c> (AC1, AC3). Los ejemplos se validan contra sus esquemas en la prueba de ejemplos.
    /// </summary>
    [Fact]
    public void AC4_ElEnvioDeAdjuntosPublicaElContratoAcordado()
    {
        const string ruta = "/api/v1/external/tramites/{id}/adjuntos";
        var post = Contrato.Value.RootElement.GetProperty("paths").GetProperty(ruta).GetProperty("post");

        post.GetProperty("requestBody").GetProperty("content").GetProperty("multipart/form-data")
            .GetProperty("schema").GetProperty("$ref").GetString().Should().Be("#/components/schemas/AdjuntoEnvio");
        Schemas.GetProperty("AdjuntoEnvio").GetProperty("properties").GetProperty("tipo").GetProperty("enum")
            .EnumerateArray().Select(t => t.GetString()).Should().Equal("liquidacion_impuesto");

        foreach (var status in new[] { "200", "201" })
        {
            post.GetProperty("responses").GetProperty(status).GetProperty("content").GetProperty("application/json")
                .GetProperty("schema").GetProperty("$ref").GetString().Should().Be("#/components/schemas/AdjuntoRecibido");
        }

        Schemas.GetProperty("AdjuntoRecibido").GetProperty("required").EnumerateArray().Select(c => c.GetString())
            .Should().BeEquivalentTo("adjuntoId", "tipo", "sha256", "reemplazoDe", "enMatriz", "pagadoMarcado");

        CodigosDocumentados(ruta, "post", 400).Should().BeEquivalentTo("missing_file", "invalid_tipo", "invalid_mime", "file_too_large");
        CodigosDocumentados(ruta, "post", 401).Should().Equal("invalid_token");
        CodigosDocumentados(ruta, "post", 403).Should().Equal("insufficient_scope");
        CodigosDocumentados(ruta, "post", 404).Should().Equal("procedure_not_found");
        CodigosDocumentados(ruta, "post", 409).Should().BeEquivalentTo("not_allowed_in_state", "not_allowed_in_state", "attachment_exists");
        CodigosDocumentados(ruta, "post", 429).Should().Equal("rate_limited");
        CodigosDocumentados(ruta, "post", 503).Should().Equal("storage_unavailable");

        var deEstado = post.GetProperty("responses").GetProperty("409").GetProperty("content").GetProperty("application/problem+json")
            .GetProperty("examples").EnumerateObject().Select(e => e.Value.GetProperty("value"))
            .Where(v => v.GetProperty("code").GetString() == "not_allowed_in_state")
            .ToList();
        deEstado.Select(v => v.GetProperty("terminal").GetBoolean()).Should().BeEquivalentTo([true, false]);
        deEstado.Select(v => v.TryGetProperty("estado", out var estado) && estado.ValueKind == JsonValueKind.String)
            .Should().AllBeEquivalentTo(true);

        Contrato.Value.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("externalClient")
            .GetProperty("description").GetString().Should().Contain("external.tramites.attachments.write");
    }

    private static IEnumerable<(string Ruta, bool Anunciada)> Operaciones(JsonElement raiz) =>
        raiz.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject().Select(o => (
                $"{o.Name.ToUpperInvariant()} {p.Name}",
                o.Value.TryGetProperty("x-estado", out var estado) && estado.GetString() == "anunciada")));

    private HashSet<string> RutasExternasDeLaApi() =>
        _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/external", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(m => $"{m} {e.RoutePattern.RawText}"))
            .ToHashSet();

    // ── Validación de un subconjunto de OpenAPI 3.0 ─────────────────────────────
    // type, nullable, required, properties (sin propiedades no declaradas), items, enum, maxItems, allOf,
    // $ref y los formatos uuid y date-time. Es lo que usa el contrato; un constructo nuevo sin soporte falla.

    private static void Validar(JsonElement valor, string esquema)
    {
        var errores = new List<string>();
        Validar(valor, Schemas.GetProperty(esquema), "$", errores);
        errores.Should().BeEmpty($"la respuesta debe cumplir components.schemas.{esquema}");
    }

    private static void Validar(JsonElement valor, JsonElement esquema, string ruta, List<string> errores)
    {
        if (esquema.TryGetProperty("$ref", out var referencia))
        {
            Validar(valor, Schemas.GetProperty(referencia.GetString()!.Split('/')[^1]), ruta, errores);
            return;
        }

        var nullable = esquema.TryGetProperty("nullable", out var n) && n.GetBoolean();
        if (valor.ValueKind == JsonValueKind.Null)
        {
            if (!nullable && !(esquema.TryGetProperty("allOf", out var todos) && todos.EnumerateArray().Any(EsNullable)))
            {
                errores.Add($"{ruta}: null no permitido");
            }

            return;
        }

        if (esquema.TryGetProperty("allOf", out var allOf))
        {
            foreach (var parte in allOf.EnumerateArray())
            {
                Validar(valor, parte, ruta, errores);
            }
        }

        foreach (var clave in esquema.EnumerateObject().Select(p => p.Name))
        {
            if (clave is not ("type" or "nullable" or "required" or "properties" or "items" or "enum" or "format" or "allOf"
                or "description" or "maxItems" or "minimum" or "maximum" or "default" or "example" or "x-pii"))
            {
                errores.Add($"{ruta}: el validador no soporta «{clave}»");
            }
        }

        if (!esquema.TryGetProperty("type", out var tipo))
        {
            return;
        }

        var esperado = tipo.GetString();
        var ok = esperado switch
        {
            "object" => valor.ValueKind == JsonValueKind.Object,
            "array" => valor.ValueKind == JsonValueKind.Array,
            "string" => valor.ValueKind == JsonValueKind.String,
            "boolean" => valor.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => valor.ValueKind == JsonValueKind.Number && valor.TryGetInt64(out _),
            "number" => valor.ValueKind == JsonValueKind.Number,
            _ => false,
        };
        if (!ok)
        {
            errores.Add($"{ruta}: se esperaba {esperado} y llegó {valor.ValueKind}");
            return;
        }

        if (esquema.TryGetProperty("enum", out var valores) && !valores.EnumerateArray().Any(v => v.GetRawText() == valor.GetRawText()))
        {
            errores.Add($"{ruta}: {valor.GetRawText()} no está en enum");
        }

        if (esperado == "string" && esquema.TryGetProperty("format", out var formato))
        {
            var texto = valor.GetString()!;
            var formatoOk = formato.GetString() switch
            {
                "uuid" => Guid.TryParse(texto, out _),
                "date-time" => DateTimeOffset.TryParseExact(texto, ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                _ => true,
            };
            if (!formatoOk)
            {
                errores.Add($"{ruta}: «{texto}» no es {formato.GetString()}");
            }
        }

        if (esperado == "array")
        {
            if (esquema.TryGetProperty("maxItems", out var max) && valor.GetArrayLength() > max.GetInt32())
            {
                errores.Add($"{ruta}: más de {max.GetInt32()} elementos");
            }

            var k = 0;
            foreach (var elemento in valor.EnumerateArray())
            {
                Validar(elemento, esquema.GetProperty("items"), $"{ruta}[{k++}]", errores);
            }
        }

        if (esperado == "object" && esquema.TryGetProperty("properties", out var propiedades))
        {
            var declaradas = propiedades.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
            foreach (var requerida in esquema.TryGetProperty("required", out var req) ? req.EnumerateArray().Select(r => r.GetString()!) : [])
            {
                if (!valor.TryGetProperty(requerida, out _))
                {
                    errores.Add($"{ruta}.{requerida}: falta");
                }
            }

            foreach (var propiedad in valor.EnumerateObject())
            {
                if (declaradas.TryGetValue(propiedad.Name, out var sub))
                {
                    Validar(propiedad.Value, sub, $"{ruta}.{propiedad.Name}", errores);
                }
                else
                {
                    errores.Add($"{ruta}.{propiedad.Name}: no está en el contrato");
                }
            }
        }
    }

    private static bool EsNullable(JsonElement parte) =>
        parte.TryGetProperty("$ref", out var r)
            ? Schemas.GetProperty(r.GetString()!.Split('/')[^1]).TryGetProperty("nullable", out var n) && n.GetBoolean()
            : parte.TryGetProperty("nullable", out var m) && m.GetBoolean();

    private static IEnumerable<(string Esquema, JsonElement Ejemplo)> Ejemplos(JsonElement paths)
    {
        foreach (var operacion in paths.EnumerateObject().SelectMany(p => p.Value.EnumerateObject()).Select(o => o.Value))
        {
            var contenidos = new List<JsonElement>();
            if (operacion.TryGetProperty("requestBody", out var cuerpo))
            {
                contenidos.Add(cuerpo.GetProperty("content"));
            }

            contenidos.AddRange(operacion.GetProperty("responses").EnumerateObject()
                .Where(r => r.Value.TryGetProperty("content", out _))
                .Select(r => r.Value.GetProperty("content")));

            foreach (var medio in contenidos.SelectMany(c => c.EnumerateObject()).Select(m => m.Value))
            {
                var esquema = medio.GetProperty("schema").GetProperty("$ref").GetString()!.Split('/')[^1];
                if (medio.TryGetProperty("example", out var ejemplo))
                {
                    yield return (esquema, ejemplo);
                }

                if (medio.TryGetProperty("examples", out var varios))
                {
                    foreach (var e in varios.EnumerateObject())
                    {
                        yield return (esquema, e.Value.GetProperty("value"));
                    }
                }
            }
        }
    }

    private static IReadOnlyList<string?> CodigosDocumentados(string path, string metodo, int status) =>
        Contrato.Value.RootElement.GetProperty("paths").GetProperty(path).GetProperty(metodo).GetProperty("responses")
            .GetProperty(status.ToString(CultureInfo.InvariantCulture)).GetProperty("content").GetProperty("application/problem+json")
            .GetProperty("examples").EnumerateObject().Select(e => e.Value.GetProperty("value").GetProperty("code").GetString()).ToList();

    private static string RutaContrato()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contracts", "openapi", "external-api.v1.json")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("las pruebas corren dentro del repositorio (contracts/openapi/external-api.v1.json)");
        return Path.Combine(dir!.FullName, "contracts", "openapi", "external-api.v1.json");
    }

    // ── Host y datos ficticios ─────────────────────────────────────────────────

    private async Task<HttpResponseMessage> Get(string url, IReadOnlyList<string> scopes)
    {
        using var scope = _factory.Services.CreateScope();
        var pase = scope.ServiceProvider.GetRequiredService<IExternalClientTokenIssuer>().Issue("contrato-test", scopes).Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pase);
        return await _factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

    private static ProcedureSyncEntry Entrada(ProcedureSyncItem item) => new(new ProcedureSyncPosition(100, item.SyncVersion), item);

    private static ProcedureSyncItem ItemCompleto() => new(
        Tramite, "FT1-0001234", 1234, 48213, DateTimeOffset.Parse("2026-09-21T10:14:55-05:00", CultureInfo.InvariantCulture), false, "aprobado",
        new ProcedureSyncTramite("MATRICULA_NUEVA", "Matrícula inicial", "MATRICULAS"),
        DateTimeOffset.Parse("2026-09-01T08:00:00-05:00", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-09-01T09:30:00-05:00", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-09-20T16:02:10-05:00", CultureInfo.InvariantCulture),
        new ProcedureSyncVehiculo("1HGBH41JXMN109186", "ABC123", "CAMIONETA", "MARCA EJEMPLO", "LINEA EJEMPLO", 2026, "SUV", 2000, null, 5,
            "MTR000000", "SER000000", new ProcedureSyncTipoServicio("PARTICULAR", "Particular")),
        new ProcedureSyncOrganismo("76520000", "SECRETARIA DE TRANSITO EJEMPLO", "76520", "PALMIRA", "VALLE DEL CAUCA"),
        [
            new ProcedureSyncComprador(1, 60.00m, "comprador", "juridical", "NIT", "900000000", "EMPRESA EJEMPLO SAS", "CALLE 1 # 2-3", "PALMIRA", "3000000000", "contacto@ejemplo.test"),
            new ProcedureSyncComprador(2, 40.00m, "comprador", "natural", "CC", "1000000000", "PERSONA EJEMPLO", "CARRERA 4 # 5-6", "PALMIRA", "3100000000", "persona@ejemplo.test"),
        ],
        new ProcedureSyncFactura(Factura, "factura.pdf", DateTimeOffset.Parse("2026-09-02T10:00:00-05:00", CultureInfo.InvariantCulture)),
        new ProcedureSyncCompania(new Guid("0189a0b1-c2d3-7e4f-a5b6-c7d8e9f0a1b2"), "901000000", "TRAMITADORA EJEMPLO SAS"));

    private static ProcedureSyncItem Tombstone() => ItemCompleto() with
    {
        Id = Guid.CreateVersion7(), SyncVersion = 48214, Eliminado = true, Estado = "anulado", FechaAprobacion = null,
        Vehiculo = null, Organismo = null, Compradores = [], Factura = null,
    };

    private static ProcedureSyncItem ItemConNulos() => ItemCompleto() with
    {
        Id = Guid.CreateVersion7(), SyncVersion = 48215, Estado = "entregado", FechaRadicacion = null, FechaAprobacion = null,
        Tramite = new ProcedureSyncTramite("OTRO", "Otro trámite", null),
        Vehiculo = new ProcedureSyncVehiculo(null, null, null, null, null, null, null, null, "1.6 L", null, null, null, null),
        Organismo = new ProcedureSyncOrganismo(null, null, null, null, null),
        Compradores = [new ProcedureSyncComprador(1, null, "propietario", null, null, null, null, null, null, null, null)],
        CompaniaGestora = new ProcedureSyncCompania(Guid.CreateVersion7(), null, null),
    };

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public FeedFijo Feed { get; } = new();

        public ClientesEnMemoria Clientes { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProcedureSyncReadRepository>();
                services.AddSingleton<IProcedureSyncReadRepository>(Feed);
                services.RemoveAll<IAttachmentStorage>();
                services.AddSingleton<IAttachmentStorage, FirmaFija>();
                services.RemoveAll<IExternalClientRepository>();
                services.AddSingleton<IExternalClientRepository>(Clientes);
                services.RemoveAll<IExternalAccessLogRepository>();
                services.AddSingleton<IExternalAccessLogRepository, SinBitacora>();
            });
    }

    public sealed class FeedFijo : IProcedureSyncReadRepository
    {
        public IReadOnlyList<ProcedureSyncEntry> Entradas { get; set; } = [];

        public Task<IReadOnlyList<ProcedureSyncEntry>> ReadItemsAsync(ProcedureSyncPageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProcedureSyncEntry>>(Entradas.Take(request.PageSize).ToList());

        public Task<IReadOnlyList<ProcedureSyncChange>> ReadChangesAsync(ProcedureSyncPageRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProcedureSyncInvoiceFile?> FindInvoiceAsync(Guid procedureId, Guid attachmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(procedureId == Tramite && attachmentId == Factura
                ? new ProcedureSyncInvoiceFile("fm-contrato", "factura.pdf", "application/pdf")
                : null);
    }

    private sealed class FirmaFija : IAttachmentStorage
    {
        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(("https://almacen.ejemplo.test/fm-contrato?firma=x", DateTimeOffset.UtcNow.AddMinutes(10)));

        public Task<StoredFile> SaveAsync(Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PresignedUpload> CreatePresignedUploadAsync(Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class SinBitacora : IExternalAccessLogRepository
    {
        public Task AddAsync(ExternalAccessLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public sealed class ClientesEnMemoria : IExternalClientRepository
    {
        private readonly ConcurrentDictionary<string, ExternalClientCredentials> _clientes = new(StringComparer.Ordinal);

        public string Alta(IServiceProvider services, string secreto)
        {
            using var scope = services.CreateScope();
            var hash = scope.ServiceProvider.GetRequiredService<IExternalClientSecretHasher>().Hash(secreto);
            var clientId = $"contrato-{Guid.NewGuid():N}"[..20];
            _clientes[clientId] = new ExternalClientCredentials(
                Guid.CreateVersion7(), clientId, hash, null, null, false, true, ExternalScopes.Todos, 0, null);
            return clientId;
        }

        public Task<ExternalClientCredentials?> GetCredentialsByClientIdAsync(string clientId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_clientes.TryGetValue(clientId, out var c) ? c : null);

        public Task<DateTimeOffset?> RegisterFailedAttemptAsync(
            Guid id, int maxFailedAttempts, TimeSpan lockDuration, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult<DateTimeOffset?>(null);

        public Task RegisterTokenIssuedAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ExternalClientView> CreateAsync(NewExternalClient client, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ExternalClientView?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ExternalClientView>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // HU #13088 — miembros de administración que estas pruebas no usan.
        public Task<ExternalClientView?> UpdateAsync(
            Guid id, ExternalClientChanges changes, Guid? actor, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ExternalClientView?> ReplaceSecretAsync(
            Guid id, string newSecretHash, bool revokePrevious, Guid? actor, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ExternalClientView?> UnlockAsync(Guid id, Guid? actor, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
