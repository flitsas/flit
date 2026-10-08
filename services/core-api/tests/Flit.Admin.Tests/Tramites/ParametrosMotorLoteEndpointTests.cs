using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #13420 (épica #13216) — borde HTTP de <c>GET/PUT /api/v1/admin/plataforma/consolidados/lotes/parametros</c> con
/// los handlers reales y el repositorio sustituido: 200 con todos los campos, 400 ProblemDetails con <c>errors</c> por
/// campo (rango, lease ≤ timeout, ausentes), 409 por <c>row_version</c>, 404 sin fila, 403 sin rol SuperAdmin y 401 sin
/// token; y el contrato OpenAPI de las dos rutas. La escritura real la cubre <c>ParametrosMotorLoteIntegrationTests</c>.
/// <para>Uso de ejemplo: <c>PUT</c> con <c>maxItemsPerBatch = 0</c> ⇒ 400 y <c>errors.maxItemsPerBatch</c>.</para>
/// </summary>
public sealed class ParametrosMotorLoteEndpointTests : IClassFixture<ParametrosMotorLoteEndpointTests.Factory>
{
    private const string Ruta = "/api/v1/admin/plataforma/consolidados/lotes/parametros";

    private static readonly Guid UsuarioId = Guid.Parse("13420000-0000-4000-8000-0000000000b1");
    private static readonly Guid TenantId = Guid.Parse("13420000-0000-4000-8000-0000000000c1");

    private readonly Factory _factory;

    public ParametrosMotorLoteEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reiniciar();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── AC1 — GET ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_Get_SuperAdmin_200_ConTodosLosCampos_QuienCambio_RowVersion_YLimites()
    {
        var response = await Cliente(Token("SuperAdmin")).GetAsync(Ruta, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var raiz = await Raiz(response);
        foreach (var (campo, valor) in new (string, int)[]
                 {
                     ("maxItemsPerBatch", 10_000), ("maxPdfsPerPart", 500), ("maxMbPerPart", 250), ("itemSlots", 2),
                     ("itemTimeoutSeconds", 300), ("itemLeaseSeconds", 600), ("maxItemAttempts", 3),
                     ("retryDelaySeconds", 30), ("partTimeoutSeconds", 1200), ("partLeaseSeconds", 1800),
                     ("maxPartAttempts", 3), ("retentionHours", 24),
                 })
            raiz.GetProperty(campo).GetInt32().Should().Be(valor, campo);
        raiz.GetProperty("isActive").GetBoolean().Should().BeTrue();
        raiz.GetProperty("updatedBy").GetGuid().Should().Be(UsuarioId);
        raiz.GetProperty("updatedByName").GetString().Should().Be("Ana Admin");
        raiz.GetProperty("updatedAt").GetDateTimeOffset().Should().Be(Factory.Actualizado);
        raiz.GetProperty("rowVersion").GetInt64().Should().Be(7);
        var limites = raiz.GetProperty("limites").EnumerateArray().ToList();
        limites.Should().HaveCount(12);
        var tope = limites.Single(l => l.GetProperty("campo").GetString() == "maxItemsPerBatch");
        tope.GetProperty("minimo").GetInt32().Should().Be(1);
        tope.GetProperty("maximo").GetInt32().Should().Be(32_766);
        limites.Single(l => l.GetProperty("campo").GetString() == "itemLeaseSeconds")
            .GetProperty("mayorQue").GetString().Should().Be("itemTimeoutSeconds");
    }

    [Fact]
    public async Task AC1_Get_SinFilaDeParametros_404_ParametrosNoEncontrados()
    {
        _factory.Repo.ObtenerAsync(Arg.Any<CancellationToken>()).Returns((ConsolidadoExportSettingsLeidos?)null);

        var response = await Cliente(Token("SuperAdmin")).GetAsync(Ruta, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Raiz(response)).GetProperty("error").GetString().Should().Be("parametros_no_encontrados");
    }

    // ── AC2 — PUT válido ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_Put_TopeA5000_200_ConLaFilaReleida_YPasaRowVersionYSub()
    {
        var response = await Cliente(Token("SuperAdmin")).PutAsync(Ruta, Cuerpo(new() { ["maxItemsPerBatch"] = 5000 }), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var raiz = await Raiz(response);
        raiz.GetProperty("maxItemsPerBatch").GetInt32().Should().Be(5000);
        raiz.GetProperty("rowVersion").GetInt64().Should().Be(8);
        await _factory.Repo.Received(1).ActualizarAsync(
            Arg.Is<ConsolidadoExportSettingsValores>(v => v.MaxItemsPerBatch == 5000 && v.IsActive),
            7, UsuarioId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // ── AC3 — 400 ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("maxItemsPerBatch", 0)]
    [InlineData("maxItemsPerBatch", 32_767)]
    [InlineData("maxMbPerPart", 9)]
    [InlineData("itemSlots", 7)]
    [InlineData("retryDelaySeconds", 4)]
    [InlineData("retentionHours", 169)]
    public async Task AC3_Put_FueraDeRango_400_ProblemDetails_ConErrorsDelCampo_SinEscribir(string campo, int valor)
    {
        var response = await Cliente(Token("SuperAdmin")).PutAsync(Ruta, Cuerpo(new() { [campo] = valor }), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var raiz = await Raiz(response);
        raiz.GetProperty("error").GetString().Should().Be("parametros_invalidos");
        raiz.GetProperty("status").GetInt32().Should().Be(400);
        raiz.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Equal(campo);
        await _factory.Repo.DidNotReceiveWithAnyArgs().ActualizarAsync(default!, default, default, default, Ct);
    }

    [Theory]
    [InlineData("itemTimeoutSeconds", "itemLeaseSeconds")]
    [InlineData("partTimeoutSeconds", "partLeaseSeconds")]
    public async Task AC3_Put_LeaseIgualAlTimeout_400_EnElCampoDelLease(string timeout, string lease)
    {
        var response = await Cliente(Token("SuperAdmin"))
            .PutAsync(Ruta, Cuerpo(new() { [timeout] = 900, [lease] = 900 }), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Raiz(response)).GetProperty("errors").GetProperty(lease)[0].GetString().Should().Contain(timeout);
        await _factory.Repo.DidNotReceiveWithAnyArgs().ActualizarAsync(default!, default, default, default, Ct);
    }

    [Fact]
    public async Task AC3_Put_SinIsActiveNiRowVersion_400_EnEsosCampos()
    {
        var response = await Cliente(Token("SuperAdmin"))
            .PutAsync(Ruta, Cuerpo(new() { ["isActive"] = null, ["rowVersion"] = null }), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Raiz(response)).GetProperty("errors").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("isActive", "rowVersion");
    }

    [Fact]
    public async Task AC3_Put_SinCuerpo_400()
    {
        var response = await Cliente(Token("SuperAdmin"))
            .PutAsync(Ruta, new StringContent("", Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── AC4 — 409 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_Put_RowVersionDesactualizado_409_RowVersionConflict()
    {
        _factory.Repo.ActualizarAsync(Arg.Any<ConsolidadoExportSettingsValores>(), Arg.Any<long>(), Arg.Any<Guid?>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ActualizarSettingsResultado(ActualizarSettingsEstado.Conflicto));

        var response = await Cliente(Token("SuperAdmin")).PutAsync(Ruta, Cuerpo(new() { ["rowVersion"] = 6 }), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await Raiz(response)).GetProperty("error").GetString().Should().Be("row_version_conflict");
    }

    // ── AC5 — 403 / 401 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("ot_admin")]
    [InlineData("Radicador")]
    public async Task AC5_GetYPut_SinRolSuperAdmin_403_SinTocarElRepositorio(string rol)
    {
        var cliente = Cliente(Token(rol));

        (await cliente.GetAsync(Ruta, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await cliente.PutAsync(Ruta, Cuerpo(new()), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.Repo.DidNotReceiveWithAnyArgs().ObtenerAsync(Ct);
        await _factory.Repo.DidNotReceiveWithAnyArgs().ActualizarAsync(default!, default, default, default, Ct);
    }

    [Fact]
    public async Task AC5_SinToken_401()
    {
        (await _factory.CreateClient().GetAsync(Ruta, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Code review épica #13216 (Obs6) — un token SuperAdmin sin <c>sub</c> ni <c>NameIdentifier</c> no puede guardar
    /// <c>updated_by = NULL</c>: 401 ProblemDetails <c>usuario_no_identificado</c> y la fila no se toca.
    /// </summary>
    [Fact]
    public async Task Obs6_Put_TokenSuperAdminSinSub_401_UsuarioNoIdentificado_SinEscribir()
    {
        var response = await Cliente(Token("SuperAdmin", conSub: false))
            .PutAsync(Ruta, Cuerpo(new() { ["maxItemsPerBatch"] = 5000 }), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync(Ct));
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var raiz = await Raiz(response);
        raiz.GetProperty("error").GetString().Should().Be("usuario_no_identificado");
        raiz.GetProperty("status").GetInt32().Should().Be(401);
        await _factory.Repo.DidNotReceiveWithAnyArgs().ActualizarAsync(default!, default, default, default, Ct);
    }

    // ── Contrato OpenAPI ───────────────────────────────────────────────────────────────

    [Fact]
    public void Contrato_LaRutaPublicaGetYPut_ConSusCodigos_YLosEsquemas()
    {
        var yaml = Yaml();
        var m = Regex.Match(yaml, "^  " + Regex.Escape(Ruta) + ":\\n((?:(?:    .*)?\\n)+)", RegexOptions.Multiline);
        m.Success.Should().BeTrue($"paths.{Ruta} debe existir en core-api.v1.yaml");
        var bloque = m.Groups[1].Value;

        bloque.Should().Contain("    get:\n").And.Contain("operationId: AdminPlataformaConsolidadoLotesGetParametros");
        bloque.Should().Contain("    put:\n").And.Contain("operationId: AdminPlataformaConsolidadoLotesPutParametros");
        foreach (var status in new[] { "\"200\"", "\"400\"", "\"401\"", "\"403\"", "\"404\"", "\"409\"" })
            bloque.Should().Contain(status);
        foreach (var codigo in new[] { "parametros_invalidos", "row_version_conflict", "parametros_no_encontrados", "SuperAdmin" })
            bloque.Should().Contain(codigo);
        // Obs6 — el 401 del PUT por token sin usuario lleva su código estable en el contrato.
        var put = bloque[bloque.IndexOf("    put:\n", StringComparison.Ordinal)..];
        put.Should().Contain("usuario_no_identificado");
        Schema(yaml, "ParametrosMotorLoteProblem").Should().Contain("usuario_no_identificado");

        var dto = Schema(yaml, "ParametrosMotorLote");
        foreach (var campo in new[]
                 {
                     "maxItemsPerBatch", "maxPdfsPerPart", "maxMbPerPart", "itemSlots", "itemTimeoutSeconds",
                     "itemLeaseSeconds", "maxItemAttempts", "retryDelaySeconds", "partTimeoutSeconds", "partLeaseSeconds",
                     "maxPartAttempts", "retentionHours", "isActive", "updatedAt", "updatedBy", "updatedByName",
                     "rowVersion", "limites",
                 })
            dto.Should().Contain(campo + ":");
        dto.Should().Contain("maximum: 32766");
        Schema(yaml, "ActualizarParametrosMotorLoteRequest").Should().Contain("rowVersion:").And.Contain("required:");
        Schema(yaml, "ParametroMotorLoteLimite").Should().Contain("mayorQue:");
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────

    private static string Yaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contracts", "openapi", "core-api.v1.yaml")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return File.ReadAllText(Path.Combine(dir!.FullName, "contracts", "openapi", "core-api.v1.yaml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Schema(string yaml, string nombre)
    {
        var m = Regex.Match(yaml, "^    " + nombre + ":\\n((?:(?:      .*)?\\n)+)", RegexOptions.Multiline);
        m.Success.Should().BeTrue($"components.schemas.{nombre} debe existir en core-api.v1.yaml");
        return m.Groups[1].Value;
    }

    /// <summary>Cuerpo válido (valores sembrados, rowVersion 7) con <paramref name="cambios"/> encima.</summary>
    private static StringContent Cuerpo(Dictionary<string, object?> cambios)
    {
        var cuerpo = new Dictionary<string, object?>
        {
            ["maxItemsPerBatch"] = 10_000,
            ["maxPdfsPerPart"] = 500,
            ["maxMbPerPart"] = 250,
            ["itemSlots"] = 2,
            ["itemTimeoutSeconds"] = 300,
            ["itemLeaseSeconds"] = 600,
            ["maxItemAttempts"] = 3,
            ["retryDelaySeconds"] = 30,
            ["partTimeoutSeconds"] = 1200,
            ["partLeaseSeconds"] = 1800,
            ["maxPartAttempts"] = 3,
            ["retentionHours"] = 24,
            ["isActive"] = true,
            ["rowVersion"] = 7L,
        };
        foreach (var (k, v) in cambios)
            cuerpo[k] = v;
        return new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json");
    }

    private HttpClient Cliente(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Raiz(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    private static string Token(string role, bool conSub = true)
    {
        var claims = new List<Claim>
        {
            new("role", role), new("role_code", role), new("tenant_id", TenantId.ToString()),
        };
        if (conSub)
            claims.Add(new Claim("sub", UsuarioId.ToString()));
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Host con el repositorio de parámetros sustituido (fila sembrada, rowVersion 7) y los handlers reales.</summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        public static readonly DateTimeOffset Actualizado = new(2026, 10, 7, 15, 30, 0, TimeSpan.Zero);

        public IConsolidadoExportSettingsRepository Repo { get; } = Substitute.For<IConsolidadoExportSettingsRepository>();

        public Factory() => Reiniciar();

        public static ConsolidadoExportSettings Fila(int tope = 10_000, long rowVersion = 7) => new()
        {
            Id = Guid.NewGuid(),
            MaxItemsPerBatch = tope,
            MaxPdfsPerPart = 500,
            MaxMbPerPart = 250,
            ItemSlots = 2,
            ItemTimeoutSeconds = 300,
            ItemLeaseSeconds = 600,
            MaxItemAttempts = 3,
            RetryDelaySeconds = 30,
            PartTimeoutSeconds = 1200,
            PartLeaseSeconds = 1800,
            MaxPartAttempts = 3,
            RetentionHours = 24,
            IsActive = true,
            UpdatedAt = Actualizado,
            UpdatedBy = UsuarioId,
            RowVersion = rowVersion,
        };

        public void Reiniciar()
        {
            Repo.ClearSubstitute();
            Repo.ObtenerAsync(Arg.Any<CancellationToken>())
                .Returns(new ConsolidadoExportSettingsLeidos(Fila(), "Ana Admin"));
            Repo.ActualizarAsync(Arg.Any<ConsolidadoExportSettingsValores>(), Arg.Any<long>(), Arg.Any<Guid?>(),
                    Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(ci => new ActualizarSettingsResultado(ActualizarSettingsEstado.Actualizado,
                    new ConsolidadoExportSettingsLeidos(
                        Fila(ci.Arg<ConsolidadoExportSettingsValores>().MaxItemsPerBatch, ci.Arg<long>() + 1), "Ana Admin")));
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services => services.AddScoped(_ => Repo));
        }
    }
}
