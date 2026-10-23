using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU #13776 — señal única de gravamen RUNT con limitaciones a la propiedad y dato prendario, y mensaje
/// del check <c>gravamenes</c> sin banderas en NO (Bug #13768 punto 2), igual para Kyverum, Verifik e Intempo.
/// Datos ficticios (Ley 1581): «ACREEDOR DE PRUEBA S.A.S.», NIT 900000001.
/// Uso de ejemplo:
/// <code>IntempoVehicleResultMapper.Map(response).Checks.Single(c =&gt; c.Key == "gravamenes")</code>
/// </summary>
public sealed class RuntGravamenSenalUnicaHu13776Tests
{
    private const string Vin = "1HGCM82633A004352";

    private static JsonElement Limitacion() =>
        JsonSerializer.Deserialize<JsonElement>(
            """{"tipoLimitacion":"PRENDA","aFavorDe":"ACREEDOR DE PRUEBA S.A.S.","documento":"900000001"}""");

    private static IntempoVehicleResponse Intempo(
        string? tieneGravamenes = "NO",
        string? prendas = "NO",
        string? prendario = "NO",
        List<object>? limitaciones = null,
        List<IntempoGravamen>? gravamenes = null) => new()
        {
            CodigoResultado = "Exitoso",
            EstadoDelVehiculo = "INACTIVO",
            NoVin = Vin,
            TieneGravamenes = tieneGravamenes,
            Prendas = prendas,
            Prendario = prendario,
            SoatNacionales = [],
            Gravamenes = gravamenes ?? [],
            LimitacionesPropiedad = limitaciones ?? [],
        };

    private static ConsultationCheck Gravamenes(ConsultationResult r) => r.Checks.Single(c => c.Key == "gravamenes");

    private static HydratedField Field(ConsultationResult r, string key) => r.HydratedFields.Single(f => f.FieldKey == key);

    /// <summary>La señal única evaluada sobre lo que hidrata el mapper, como la vería el trámite.</summary>
    private static bool Senal(ConsultationResult r) =>
        RuntGravamenSignal.Reporta(r.HydratedFields.Select(f =>
            new ProcedureInstanceFieldValue { FieldKey = f.FieldKey, ValueText = f.ValueText, ValueJson = f.ValueJson }));

    // ── AC1 — Intempo con limitaciones: se conserva el JSON y la señal reporta ───────────────────

    [Fact]
    public void AC1_Intempo_ConLimitaciones_HidrataElJsonEnValueJson_YLaSenalReporta()
    {
        var result = IntempoVehicleResultMapper.Map(Intempo(limitaciones: [Limitacion()]));

        var campo = Field(result, "runt_limitaciones_propiedad");
        campo.ValueText.Should().BeNull();
        using var json = JsonDocument.Parse(campo.ValueJson!);
        json.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        json.RootElement.GetArrayLength().Should().Be(1);
        json.RootElement[0].GetProperty("aFavorDe").GetString().Should().Be("ACREEDOR DE PRUEBA S.A.S.");
        Senal(result).Should().BeTrue();
    }

    [Fact]
    public void AC1_Intempo_SinLimitaciones_EscribeArrayVacio_ParaPisarLaConsultaAnterior()
    {
        var result = IntempoVehicleResultMapper.Map(Intempo());

        Field(result, "runt_limitaciones_propiedad").ValueJson.Should().Be("[]");
        Field(result, "runt_prendario").ValueText.Should().Be("NO");
        Senal(result).Should().BeFalse();
    }

    private sealed class StubProvider(string key, ConsultationResult result) : IConsultationProvider
    {
        public string Key => key;
        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct) =>
            Task.FromResult(result with { Provider = key });
    }

    private sealed class StaticRegistry(Dictionary<string, IConsultationProvider> providers) : IConsultationProviderRegistry
    {
        public IConsultationProvider? Resolve(string providerKey) =>
            providers.TryGetValue(providerKey, out var p) ? p : null;
    }

    private sealed class NullOverrideProvider : IConsultationTenantOverrideProvider
    {
        public Task<ConsultationTenantOverride?> GetAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult<ConsultationTenantOverride?>(null);
    }

    private static ProcedureInstance Matricula()
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013776",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            FieldKey = "vin",
            ValueText = Vin,
            Source = "consulta",
        });
        return instance;
    }

    private static async Task Consultar(IProcedureInstanceRepository repo, ProcedureInstance instance, string providerKey, ConsultationResult result)
    {
        var ct = TestContext.Current.CancellationToken;
        repo.GetByIdWithWizardGraphAsync(instance.Id, instance.TenantId, ct).Returns(instance);
        var registry = new StaticRegistry(new Dictionary<string, IConsultationProvider>
        {
            [providerKey] = new StubProvider(providerKey, result),
        });
        var options = new ConsultationChainOptions();
        options.DefaultChains[ConsultationKindKeys.VehicleVin] = [providerKey];
        options.DefaultChains[ConsultationKindKeys.VehiclePlate] = [providerKey];
        var preflight = new RunPreflightHandler(
            repo, registry, new ConsultationProviderChainResolver(registry, options),
            new NullOverrideProvider(), NullConsultationRestrictionPolicy.Instance, NullTransitOfficeResolver.Instance);

        var (_, error, _, _) = await preflight.HandleAsync(instance.Id, instance.TenantId, ct);
        error.Should().BeNull();
    }

    [Fact]
    public async Task AC1_GuardarResultadoIntempo_ConservaElJsonEnElTramite_YUnaReconsultaSinDatoLoPisa()
    {
        var repo = Substitute.For<IProcedureInstanceRepository>();
        var instance = Matricula();

        await Consultar(repo, instance, "intempo", IntempoVehicleResultMapper.Map(Intempo(limitaciones: [Limitacion()])));

        var guardado = instance.FieldValues.Single(f => f.FieldKey == "runt_limitaciones_propiedad");
        guardado.ValueJson.Should().Contain("ACREEDOR DE PRUEBA S.A.S.");
        RuntGravamenSignal.Reporta(instance.FieldValues).Should().BeTrue();

        // Re-consulta por Verifik (no trae limitaciones ni prendario): las claves de la señal se pisan.
        const string json = """
            { "data": { "informacionGeneral": { "noVin": "1HGCM82633A004352", "estadoDelVehiculo": "INACTIVO" },
                        "soat": [], "tecnoMecanica": [], "garantiasMobiliarias": [] } }
            """;
        await Consultar(repo, instance, "verifik", VerifikResultMapper.MapVehicle(JsonSerializer.Deserialize<VerifikVehicleResponse>(json)!));

        instance.FieldValues.Single(f => f.FieldKey == "runt_limitaciones_propiedad").ValueJson.Should().Be("[]");
        RuntGravamenSignal.Reporta(instance.FieldValues).Should().BeFalse();
    }

    // ── AC4 — Intempo: warn si y solo si la señal reporta ────────────────────────────────────────

    public static TheoryData<string, IntempoVehicleResponse> CasosIntempo => new()
    {
        { "nada", Intempo() },
        { "prendas SI", Intempo(prendas: "SI") },
        { "gravamenes SI", Intempo(tieneGravamenes: "SI") },
        { "prendario SI", Intempo(prendario: "SI") },
        { "limitaciones", Intempo(limitaciones: [Limitacion()]) },
        { "limitacion vacia", Intempo(limitaciones: [JsonSerializer.Deserialize<JsonElement>("{}")]) },
        { "garantia con dato", Intempo(gravamenes: [new IntempoGravamen { IdPrenda = 1000001, NombreAcreedor = "ACREEDOR DE PRUEBA S.A.S." }]) },
        { "garantia sin dato", Intempo(gravamenes: [new IntempoGravamen()]) },
        { "banderas vacias", Intempo(tieneGravamenes: null, prendas: null, prendario: null) },
    };

    [Theory]
    [MemberData(nameof(CasosIntempo))]
    public void AC4_Intempo_CheckEnWarnSiYSoloSiLaSenalReporta(string caso, IntempoVehicleResponse response)
    {
        var result = IntempoVehicleResultMapper.Map(response);

        (Gravamenes(result).Status == "warn").Should().Be(Senal(result), $"caso «{caso}»");
    }

    [Fact]
    public void AC4_Intempo_SoloLimitaciones_EsWarn()
    {
        Gravamenes(IntempoVehicleResultMapper.Map(Intempo(limitaciones: [Limitacion()]))).Status.Should().Be("warn");
        Gravamenes(IntempoVehicleResultMapper.Map(Intempo(limitaciones: [JsonSerializer.Deserialize<JsonElement>("{}")])))
            .Status.Should().Be("ok", "un elemento sin dato no es limitación");
    }

    // ── AC8 — mensaje con solo las fuentes afirmativas, en el orden fijado ───────────────────────

    [Theory]
    [InlineData("kyverum_runt")]
    [InlineData("verifik")]
    public void AC8_KyverumVerifik_GarantiasConBanderasNo_NoMuestraBanderasEnNo(string provider)
    {
        var check = RuntGarantiasMobiliarias.BuildCheck(provider, gravamenes: "NO", prendas: "NO", garantias: 1);

        check.Status.Should().Be("warn");
        check.Message.Should().Be("El RUNT reporta prenda o gravamen: 1 garantía(s) mobiliaria(s)");
        check.Message.Should().NotContain("prendas: NO").And.NotContain("gravámenes: NO");
    }

    [Fact]
    public void AC8_TodasLasFuentes_EnElOrdenFijado()
    {
        var check = Gravamenes(IntempoVehicleResultMapper.Map(Intempo(
            tieneGravamenes: "SI", prendas: "SÍ", prendario: "SI", limitaciones: [Limitacion()],
            gravamenes:
            [
                new IntempoGravamen { IdPrenda = 1000001, NombreAcreedor = "ACREEDOR DE PRUEBA S.A.S." },
                new IntempoGravamen { IdPrenda = 1000002, NombreAcreedor = "ACREEDOR DE PRUEBA S.A.S." },
            ])));

        check.Message.Should().Be(
            "El RUNT reporta prenda o gravamen: prendas: SI · gravámenes: SI · 2 garantía(s) mobiliaria(s) · dato prendario · limitaciones a la propiedad");
    }

    [Fact]
    public void AC8_Intempo_LimitacionesConBanderasNo_UsaElMismoArmador()
    {
        var check = Gravamenes(IntempoVehicleResultMapper.Map(Intempo(limitaciones: [Limitacion()])));

        check.Message.Should().Be("El RUNT reporta prenda o gravamen: limitaciones a la propiedad");
    }

    [Fact]
    public void AC8_Intempo_GarantiaYPrendario_ConBanderasNo()
    {
        var check = Gravamenes(IntempoVehicleResultMapper.Map(Intempo(
            prendario: "SI",
            gravamenes: [new IntempoGravamen { IdPrenda = 1000001, NombreAcreedor = "ACREEDOR DE PRUEBA S.A.S." }])));

        check.Message.Should().Be("El RUNT reporta prenda o gravamen: 1 garantía(s) mobiliaria(s) · dato prendario");
    }

    [Fact]
    public void AC8_SinFuentesAfirmativas_ConservaLosMensajesOkYUnknown()
    {
        RuntGarantiasMobiliarias.BuildCheck("verifik", "NO", "NO", 0).Message
            .Should().Be("Sin gravámenes ni prendas registradas en el RUNT");
        RuntGarantiasMobiliarias.BuildCheck("verifik", null, null, 0).Message
            .Should().Be("Sin información de gravámenes");
    }
}
