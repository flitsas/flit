using System.Text.Json;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Application.GeneracionDocumental.Prefill;
using Flit.Admin.Domain.GeneracionDocumental;
using Flit.Api.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// HU #12206 — prellenado «placa primero» del bloque de vehículo (CF-25).
///
/// Uso de ejemplo:
/// <code>
/// var handler = new PrefillVehiculoHandler(lookup);
/// var r = await handler.HandleAsync(new PrefillVehiculoCommand(tenant, "ABC123", "CC", "71234567"));
/// // r.Found == true, r.Fields con 12 variables del anexo, cada una con Source = "RUNT"
/// </code>
/// </summary>
public sealed class PrefillVehiculoHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Autor = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly FakeStandaloneVehiclePrefill _lookup = new();

    private PrefillVehiculoHandler Sut => new(_lookup);

    // ── AC: 12 de las 13 variables del anexo ────────────────────────────────────────

    [Fact]
    public async Task PlacaConAntecedente_Hidrata12DeLas13VariablesDelAnexo()
    {
        var result = await Sut.HandleAsync(
            new PrefillVehiculoCommand(Tenant, "abc123", "CC", "71234567"),
            TestContext.Current.CancellationToken);

        result.Found.Should().BeTrue();
        result.Source.Should().Be(PrefillSources.Runt);
        result.Error.Should().BeNull();
        result.Fields.Should().HaveCount(12);

        result.Fields.Select(f => f.Key).Should().BeEquivalentTo(
        [
            "placa", "marca", "linea", "modelo_anio", "clase_vehiculo", "tipo_carroceria",
            "color", "no_motor", "no_chasis", "no_serie", "servicio", "organismo_transito",
        ]);
    }

    [Fact]
    public void LaVariableQueFaltaEsLaLicenciaDeTransito()
    {
        // El anexo (§5.1) declara 13 variables de vehículo. La que ninguna consulta entrega es el
        // número de la licencia de tránsito: está en el cartón físico, no en el RUNT.
        PrefillVehiculoHandler.VariablesDelAnexo.Should().HaveCount(13);
        PrefillVehiculoHandler.VariableManual.Should().Be("no_licencia_transito");
    }

    [Fact]
    public async Task LaLicenciaDeTransitoNuncaSePrellena()
    {
        var result = await Sut.HandleAsync(
            new PrefillVehiculoCommand(Tenant, "ABC123", null, null),
            TestContext.Current.CancellationToken);

        result.Fields.Should().NotContain(f => f.Key == PrefillVehiculoHandler.VariableManual);
    }

    [Fact]
    public async Task CadaCampoDeclaraSuFuente()
    {
        var result = await Sut.HandleAsync(
            new PrefillVehiculoCommand(Tenant, "ABC123", null, null),
            TestContext.Current.CancellationToken);

        result.Fields.Should().OnlyContain(f => f.Source == PrefillSources.Runt);
        result.Attempts.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(
                new PrefillSourceAttempt(PrefillSources.Runt, PrefillOutcomes.Found, null));
    }

    [Fact]
    public async Task NoChasis_CaeAlVinCuandoElRuntNoReportaChasis()
    {
        var campos = new Dictionary<string, string?>(FakeStandaloneVehiclePrefill.RuntCompleto, StringComparer.OrdinalIgnoreCase);
        campos.Remove("vehicle_chassis");
        _lookup.Result = new StandaloneVehicleLookupResult(true, campos, "kyverum_runt", null);

        var result = await Sut.HandleAsync(
            new PrefillVehiculoCommand(Tenant, "ABC123", null, null),
            TestContext.Current.CancellationToken);

        result.Fields.Should().Contain(f => f.Key == "no_chasis" && f.Value == "9GAJC1234K5678901");
    }

    [Fact]
    public async Task LaPlacaSeNormalizaAMayusculasAntesDeConsultar()
    {
        await Sut.HandleAsync(
            new PrefillVehiculoCommand(Tenant, "  abc123 ", null, null),
            TestContext.Current.CancellationToken);

        _lookup.LastPlate.Should().Be("ABC123");
    }

    // ── AC: un trámite activo NO bloquea el prellenado ──────────────────────────────

    [Fact]
    public async Task PlacaConTramiteActivo_DevuelveLosCamposHidratados()
    {
        // El puerto de consulta no sabe —ni puede saber— si hay un trámite abierto: no recibe
        // instancia ni repositorio. La placa de este caso es la misma que en el wizard produciría un
        // 409 por duplicidad; aquí hidrata.
        var result = await Sut.HandleAsync(
            new PrefillVehiculoCommand(Tenant, "ABC123", "CC", "71234567"),
            TestContext.Current.CancellationToken);

        result.Found.Should().BeTrue();
        result.Fields.Should().HaveCount(12);
        result.Error.Should().BeNull("un trámite activo no es un error del prellenado");
    }

    [Fact]
    public void ElHandlerNoTienePorDondeEvaluarDuplicidadNiOrganismo()
    {
        // Estructural, no aspiracional: la única dependencia es el puerto de consulta. Sin
        // repositorio de instancias y sin resolutor de organismos no hay gate que aplicar.
        var dependencias = typeof(PrefillVehiculoHandler)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        dependencias.Should().ContainSingle().Which.Should().Be<IStandaloneVehiclePrefill>();
    }

    [Fact]
    public void ElAdaptadorNoReutilizaElPreflightDelWizard()
    {
        // RunPreflightPreviewHandler evalúa el gate de organismo y corta con 409 por duplicidad. El
        // AC prohíbe reutilizarlo: se verifica sobre la fuente, que es donde se rompería.
        var fuente = File.ReadAllText(SourcePath(
            "src", "Flit.Infrastructure", "Consultations", "StandaloneVehiclePrefillAdapter.cs"));

        fuente.Should().NotContain("RunPreflightPreviewHandler(");
        fuente.Should().NotContain("PreflightPreviewRequest");
        fuente.Should().NotContain("IProcedureInstanceRepository");
        fuente.Should().NotContain("IOtOperabilityGate");
        fuente.Should().Contain("IConsultationProviderChainResolver", "el AC exige reusar el chain resolver existente");
    }

    [Fact]
    public void ElPrellenadoDeVehiculoNoEscribeUnParserPropio()
    {
        var fuente = File.ReadAllText(SourcePath(
            "src", "Flit.Infrastructure", "Consultations", "StandaloneVehiclePrefillAdapter.cs"));

        // La respuesta cruda del RUNT la traduce KyverumRuntVehicleResultMapper DENTRO del provider:
        // el adaptador solo consume ConsultationResult.HydratedFields. Si alguien empezara a
        // deserializar la respuesta del proveedor aquí, aparecería un JsonSerializer.
        fuente.Should().NotContain("JsonSerializer");
        fuente.Should().NotContain("KyverumRuntVehicleResponse");
    }

    // ── AC: degradación ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlacaSinAntecedente_DegradaCon200YFoundFalse()
    {
        _lookup.Result = new StandaloneVehicleLookupResult(
            false, new Dictionary<string, string?>(), "kyverum_runt", null);

        var result = await Sut.HandleAsync(
            new PrefillVehiculoCommand(Tenant, "ZZZ999", null, null),
            TestContext.Current.CancellationToken);

        result.Found.Should().BeFalse();
        result.Fields.Should().BeEmpty();
        result.Error.Should().BeNull("sin coincidencia NO es 404 ni 502");
        result.Attempts.Should().ContainSingle()
            .Which.Outcome.Should().Be(PrefillOutcomes.NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PlacaVacia_NoGastaConsulta(string? placa)
    {
        var result = await Sut.HandleAsync(
            new PrefillVehiculoCommand(Tenant, placa, null, null),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be("invalid_request");
        _lookup.Calls.Should().Be(0);
    }

    // ── AC: el prellenado no persiste ───────────────────────────────────────────────

    [Fact]
    public void ElPrellenadoNoTienePorDondePersistir()
    {
        var dependencias = typeof(PrefillVehiculoHandler)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        dependencias.Should().NotContain(typeof(IStandaloneDocumentRepository));
        dependencias.Should().NotContain(typeof(IStandaloneDocumentStorage));
    }

    // ── Contrato HTTP ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElEndpointDevuelve200ConLosCamposYSusFuentes()
    {
        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.PrefillVehiculoAsync(
            ctx,
            new AdminGeneracionDocumentalEndpoints.PrefillVehiculoRequest("ABC123", "CC", "71234567"),
            Sut,
            TestContext.Current.CancellationToken);

        var body = await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("found").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("fields").GetArrayLength().Should().Be(12);
        json.RootElement.GetProperty("fields")[0].GetProperty("source").GetString().Should().Be("RUNT");

        // La respuesta del prellenado NUNCA trae id de documento: no se creó ninguno.
        json.RootElement.TryGetProperty("id", out _).Should().BeFalse();
    }

    [Fact]
    public async Task FuenteCaida_Responde502SinInventarCampos()
    {
        _lookup.Result = new StandaloneVehicleLookupResult(
            false, new Dictionary<string, string?>(), null, "provider_unavailable");

        var ctx = NewContext();

        var result = await AdminGeneracionDocumentalEndpoints.PrefillVehiculoAsync(
            ctx,
            new AdminGeneracionDocumentalEndpoints.PrefillVehiculoRequest("ABC123", null, null),
            Sut,
            TestContext.Current.CancellationToken);

        await Execute(result, ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }

    [Fact]
    public void LasTresRutasDePrellenadoExigenPermiso()
    {
        var fuente = File.ReadAllText(SourcePath(
            "src", "Flit.Api", "Endpoints", "AdminGeneracionDocumentalEndpoints.cs"));

        foreach (var ruta in new[] { "/prefill/vehiculo", "/prefill/persona-juridica", "/prefill/persona-natural" })
        {
            fuente.Should().Contain($"MapPost(\"{ruta}\"");
        }

        System.Text.RegularExpressions.Regex
            .Matches(fuente, @"\.RequirePermission\(""generacion-documental\.generate""\)")
            .Should().HaveCount(5, "preview, generate y los tres prellenados");
    }

    private static DefaultHttpContext NewContext()
    {
        var claims = new List<Claim>
        {
            new("sub", Autor.ToString()),
            new("tenant_id", Tenant.ToString()),
        };

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

    internal static string SourcePath(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        return Path.Combine([dir!.FullName, "services", "core-api", .. segments]);
    }
}
