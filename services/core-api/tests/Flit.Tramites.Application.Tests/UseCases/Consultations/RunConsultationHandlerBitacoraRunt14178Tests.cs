using System.Text.Json;
using Flit.Tramites.Application.Tracking;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Certifications;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU #14178 — la consulta de vehículo en RUNT deja una frase corta en el historial.
/// El anterior va vacío, <c>CreatedBy</c> queda null y el payload no guarda la respuesta del proveedor.
/// </summary>
public sealed class RunConsultationHandlerBitacoraRunt14178Tests
{
    private const string DocumentoProveedor = "9005551234";
    private const string PlacaAjena = "ZZZ999";
    private const string CuerpoProveedor =
        "{\"body\":{\"documento\":\"9005551234\"},\"score\":0.87,\"raw\":\"placa-ajena-ZZZ999\"}";

    private readonly IProcedureInstanceRepository _instanceRepo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    private readonly IConsultationProviderRegistry _registry = Substitute.For<IConsultationProviderRegistry>();
    private readonly IExternalQueryCacheRepository _cacheRepo = Substitute.For<IExternalQueryCacheRepository>();
    private readonly IPersonDataConsentRepository _consentRepo = Substitute.For<IPersonDataConsentRepository>();
    private readonly RunConsultationHandler _sut;

    public RunConsultationHandlerBitacoraRunt14178Tests()
    {
        var cache = new ExternalQueryCacheService(_cacheRepo, _consentRepo, _catalogRepo);
        _sut = new RunConsultationHandler(_instanceRepo, _catalogRepo, _registry, cache);
    }

    [Fact]
    public async Task AC1_VehiculoEncontrado_EscribeAnteriorVacioYCreatedByNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId, instance) = TramiteConPlaca("ABC123");
        PrepararConsulta(id, tenantId, instance, ct);

        var providerResult = Resultado(
            "green",
            [new ConsultationCheck("estado_vehiculo", "Estado", "ok", "kyverum_runt", "ACTIVO")],
            [new HydratedField("plate", "ABC123", null), new HydratedField("vehicle_brand", "TOYOTA", null)]);
        _registry.Resolve("kyverum_runt").Returns(new FakeProvider("kyverum_runt", providerResult));

        ProcedureInstanceEvent? captured = null;
        _instanceRepo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(e => captured = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "RUNT_VEHICLE", ct: ct);

        error.Should().BeNull();
        result.Should().BeSameAs(providerResult);
        captured.Should().NotBeNull();
        captured!.Tipo.Should().Be(ProcedureFieldChangeRecorder.EventType);
        captured.CreatedBy.Should().BeNull();
        captured.TenantId.Should().Be(tenantId);
        captured.ProcedureInstanceId.Should().Be(id);
        captured.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        captured.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, precision: TimeSpan.FromSeconds(5));

        var payload = JsonDocument.Parse(captured.Payload).RootElement;
        Propiedades(payload).Should().BeEquivalentTo(["etiqueta", "campo", "anterior", "nuevo"]);
        payload.GetProperty("etiqueta").GetString().Should().Be("Consulta RUNT");
        payload.GetProperty("campo").GetString().Should().Be("consulta_runt");
        payload.GetProperty("anterior").GetString().Should().Be("");
        payload.GetProperty("nuevo").GetString().Should().Be("RUNT: vehículo encontrado");
        NoTraeRespuestaDelProveedor(captured.Payload);
    }

    [Fact]
    public async Task AC1_Discrepancia_EscribeFraseCortaSinLaPlacaAjena()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId, instance) = TramiteConPlaca("ABC123");
        PrepararConsulta(id, tenantId, instance, ct);

        var providerResult = Resultado(
            "green",
            [new ConsultationCheck("estado_vehiculo", "Estado", "ok", "kyverum_runt", "ACTIVO")],
            [new HydratedField("plate", PlacaAjena, null)]);
        _registry.Resolve("kyverum_runt").Returns(new FakeProvider("kyverum_runt", providerResult));

        ProcedureInstanceEvent? captured = null;
        _instanceRepo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(e => captured = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var (_, error) = await _sut.HandleAsync(id, tenantId, "RUNT_VEHICLE", ct: ct);

        error.Should().BeNull();
        captured.Should().NotBeNull();
        captured!.CreatedBy.Should().BeNull();
        var payload = JsonDocument.Parse(captured.Payload).RootElement;
        payload.GetProperty("anterior").GetString().Should().Be("");
        payload.GetProperty("campo").GetString().Should().Be("consulta_runt");
        payload.GetProperty("nuevo").GetString().Should().Be("RUNT: con discrepancia");
        NoTraeRespuestaDelProveedor(captured.Payload);
        captured.Payload.Should().NotContain("ABC123");
    }

    [Fact]
    public async Task AC2_ConsultaFallida_EscribeFraseCortaSinJsonDelProveedor()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId, instance) = TramiteConPlaca("ABC123");
        PrepararConsulta(id, tenantId, instance, ct);

        var providerResult = Resultado(
            "red",
            [new ConsultationCheck("provider", "Consulta de vehículo", "error", "kyverum_runt", CuerpoProveedor)],
            [new HydratedField("plate", PlacaAjena, CuerpoProveedor)]);
        _registry.Resolve("kyverum_runt").Returns(new FakeProvider("kyverum_runt", providerResult));

        ProcedureInstanceEvent? captured = null;
        _instanceRepo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(e => captured = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var (result, error) = await _sut.HandleAsync(id, tenantId, "RUNT_VEHICLE", ct: ct);

        error.Should().BeNull();
        result.Should().BeSameAs(providerResult);
        captured.Should().NotBeNull();
        captured!.CreatedBy.Should().BeNull();
        var payload = JsonDocument.Parse(captured.Payload).RootElement;
        payload.GetProperty("nuevo").GetString().Should().Be("RUNT: consulta fallida");
        payload.GetProperty("anterior").GetString().Should().Be("");
        payload.GetProperty("campo").GetString().Should().Be("consulta_runt");
        NoTraeRespuestaDelProveedor(captured.Payload);
    }

    [Fact]
    public async Task AC2_ProveedorLanza_PasaPorLaMismaBitacoraSinElCuerpo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId, instance) = TramiteConPlaca("ABC123");
        PrepararConsulta(id, tenantId, instance, ct);
        _registry.Resolve("kyverum_runt").Returns(new ThrowingProvider(new InvalidOperationException(CuerpoProveedor)));

        ProcedureInstanceEvent? captured = null;
        _instanceRepo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(e => captured = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var act = async () => await _sut.HandleAsync(id, tenantId, "RUNT_VEHICLE", ct: ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
        captured.Should().NotBeNull();
        captured!.CreatedBy.Should().BeNull();
        captured.Tipo.Should().Be(ProcedureFieldChangeRecorder.EventType);
        var payload = JsonDocument.Parse(captured.Payload).RootElement;
        payload.GetProperty("nuevo").GetString().Should().Be("RUNT: consulta fallida");
        payload.GetProperty("anterior").GetString().Should().Be("");
        NoTraeRespuestaDelProveedor(captured.Payload);
        await _instanceRepo.Received(1).AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), ct);
    }

    [Fact]
    public async Task MismaFraseOtraVez_NoLlamaAddEventAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, tenantId, instance) = TramiteConPlaca("ABC123");
        instance.Events.Add(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = ProcedureFieldChangeRecorder.EventType,
            // Espacio final: ya normalizado (trim) es la misma frase, comparación Ordinal.
            Payload = """{"etiqueta":"Consulta RUNT","campo":"consulta_runt","anterior":"","nuevo":"RUNT: vehículo encontrado "}""",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CreatedBy = null,
        });
        PrepararConsulta(id, tenantId, instance, ct);
        _registry.Resolve("kyverum_runt").Returns(new FakeProvider(
            "kyverum_runt",
            Resultado("green", [], [new HydratedField("plate", "ABC123", null)])));

        var (_, error) = await _sut.HandleAsync(id, tenantId, "RUNT_VEHICLE", ct: ct);

        error.Should().BeNull();
        await _instanceRepo.DidNotReceive().AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConsultaDeActor_NoEscribeBitacoraDeVehiculo()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instancia(id, tenantId);
        _instanceRepo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);
        _catalogRepo.GetConsultationTemplateByCodeAsync("RUNT_ACTOR_NATURAL", ct)
            .Returns(Plantilla("RUNT_ACTOR_NATURAL", "actor"));
        _registry.Resolve("kyverum_runt").Returns(new FakeProvider(
            "kyverum_runt",
            Resultado("green", [], [new HydratedField("person_full_name", "Ana", null)])));

        var (_, error) = await _sut.HandleAsync(id, tenantId, "RUNT_ACTOR_NATURAL", ct: ct);

        error.Should().BeNull();
        await _instanceRepo.DidNotReceive().AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
    }

    private void PrepararConsulta(Guid id, Guid tenantId, ProcedureInstance instance, CancellationToken ct)
    {
        _instanceRepo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);
        _catalogRepo.GetConsultationTemplateByCodeAsync("RUNT_VEHICLE", ct)
            .Returns(Plantilla("RUNT_VEHICLE", "vehicle"));
    }

    private static (Guid Id, Guid TenantId, ProcedureInstance Instance) TramiteConPlaca(string placa)
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instancia(id, tenantId);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            FieldKey = "plate",
            ValueText = placa,
            Source = "user",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return (id, tenantId, instance);
    }

    private static ProcedureInstance Instancia(Guid id, Guid tenantId) =>
        new()
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000178",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ConsultationTemplate Plantilla(string code, string entityScope) =>
        new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            EntityScope = entityScope,
            ExternalRefs = """{"provider":"kyverum_runt"}""",
        };

    private static ConsultationResult Resultado(
        string overall,
        IReadOnlyList<ConsultationCheck> checks,
        IReadOnlyList<HydratedField> fields) =>
        new(
            "kyverum_runt",
            overall,
            checks,
            fields,
            RawPayload: new RawProviderPayload(
                "kyverum_runt",
                RawProviderPayload.VehicleSubject,
                PlacaAjena,
                CuerpoProveedor,
                DateTimeOffset.UtcNow));

    private static List<string> Propiedades(JsonElement payload) =>
        payload.EnumerateObject().Select(p => p.Name).ToList();

    private static void NoTraeRespuestaDelProveedor(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var nombres = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        nombres.Should().NotContain("body");
        nombres.Should().NotContain("raw");
        nombres.Should().NotContain("score");
        payload.Should().NotContain(DocumentoProveedor);
        payload.Should().NotContain(PlacaAjena);
        payload.Should().NotContain("score");
        payload.Should().NotContain("body");
        payload.Should().NotContain("raw");
    }

    private sealed class FakeProvider(string key, ConsultationResult result) : IConsultationProvider
    {
        public string Key => key;

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingProvider(Exception exception) : IConsultationProvider
    {
        public string Key => "kyverum_runt";

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct) =>
            Task.FromException<ConsultationResult>(exception);
    }
}
