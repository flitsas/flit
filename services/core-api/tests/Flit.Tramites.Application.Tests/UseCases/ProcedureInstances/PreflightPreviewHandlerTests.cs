using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// CF-02 (HU #10879, AC3/AC4) — la consulta del PASO 1 corre SIN trámite creado y aplica las mismas
/// precondiciones que el preflight de una instancia: duplicidad en proceso (CF-01) y estado registral
/// del vehículo (CF-03) bloquean ANTES de que exista registro alguno.
/// </summary>
public sealed class PreflightPreviewHandlerTests
{
    private const string VinLibre = "1HGCM82633A004352";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly InMemoryPreflightPreviewStore _store = new();

    private sealed class StubProvider(string key, ConsultationResult result) : IConsultationProvider
    {
        public int Calls { get; private set; }
        public string Key => key;

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(result with { Provider = key });
        }
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

    /// <summary>
    /// HU #11199 — resolver que acepta una única secretaría habilitada. La matrícula inicial exige
    /// secretaría desde el paso 1, así que estos tests la mandan siempre (ver <c>MatriculaRequest</c>).
    /// </summary>
    private sealed class SingleOfficeResolver(Guid enabledId) : ITransitOfficeResolver
    {
        public Task<ResolvedTransitOffice?> ResolveEnabledByNameAsync(
            Guid tenantId, string transitOfficeName, CancellationToken ct = default) =>
            Task.FromResult<ResolvedTransitOffice?>(null);

        public Task<ResolvedTransitOffice?> ResolveEnabledByIdAsync(
            Guid tenantId, Guid transitOfficeId, CancellationToken ct = default) =>
            Task.FromResult(transitOfficeId == enabledId
                ? new ResolvedTransitOffice(enabledId, "05001000", "Secretaría de Medellín", "05001")
                : null);
    }

    private static readonly Guid SecretariaHabilitada = Guid.Parse("11111111-1111-1111-1111-111111111199");

    private RunPreflightPreviewHandler HandlerWith(params (string key, IConsultationProvider provider)[] providers)
    {
        var registry = new StaticRegistry(providers.ToDictionary(p => p.key, p => p.provider));
        return new RunPreflightPreviewHandler(
            _repo,
            registry,
            new ConsultationProviderChainResolver(registry, new ConsultationChainOptions()),
            new NullOverrideProvider(),
            NullConsultationRestrictionPolicy.Instance,
            _store,
            new SingleOfficeResolver(SecretariaHabilitada),
            NullConsultationBlockingPolicy.Instance);
    }

    private static ConsultationResult VehiculoOk(params HydratedField[] fields) =>
        new("stub", "green",
            [new ConsultationCheck("estado_vehiculo", "Estado del vehículo", "fail", "stub", "REGISTRADO")],
            fields);

    private static PreflightPreviewRequest MatriculaRequest(Guid tenantId, string vin = VinLibre) =>
        new(tenantId, TramiteModalidadEntradaCodes.MatriculaInicial, vin, null, null, null, SecretariaHabilitada);

    [Fact]
    public async Task Preview_MatriculaSinConflictos_DevuelveSemaforoYToken()
    {
        var tenant = Guid.NewGuid();
        _repo.FindTramitesByVinAsync(tenant, Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var provider = new StubProvider("kyverum_runt", VehiculoOk(new HydratedField("vehicle_brand", "RENAULT", null)));
        var handler = HandlerWith(("kyverum_runt", provider));

        var (result, error, _, _) = await handler.HandleAsync(MatriculaRequest(tenant), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.PreviewToken.Should().NotBeNullOrWhiteSpace();
        result.VehicleFields.Should().ContainSingle(f => f.FieldKey == "vehicle_brand" && f.ValueText == "RENAULT");
        // No hay instancia: el handler no toca ningún método de escritura del repositorio.
        await _repo.DidNotReceiveWithAnyArgs().AddPreflightSnapshotAsync(default!, TestContext.Current.CancellationToken);
        await _repo.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Preview_VinConTramiteEnProceso_BloqueaSinCrearNada()
    {
        var tenant = Guid.NewGuid();
        var existente = Guid.NewGuid();
        _repo.FindTramitesByVinAsync(tenant, Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([new VinTramiteExistente(existente, TramiteEstado.Borrador, 1, null, VinLibre, "Bogotá", DateTimeOffset.UtcNow)]);
        var provider = new StubProvider("kyverum_runt", VehiculoOk());
        var handler = HandlerWith(("kyverum_runt", provider));

        var (result, error, existingId, _) = await handler.HandleAsync(
            MatriculaRequest(tenant), TestContext.Current.CancellationToken);

        error.Should().Be(InitialProcedureValidationGate.DuplicateActiveProcedure);
        existingId.Should().Be(existente);
        result.Should().BeNull();
        await _repo.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
        // La duplicidad se decide con la llave, sin gastar la consulta externa.
        provider.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Preview_VehiculoYaMatriculadoEnRunt_BloqueaConEstadoRegistral()
    {
        var tenant = Guid.NewGuid();
        _repo.FindTramitesByVinAsync(tenant, Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
        // "ok" en matrícula inicial = el RUNT reporta el vehículo ACTIVO ⇒ ya matriculado (CF-03 AC1).
        var activo = new ConsultationResult("stub", "green",
            [new ConsultationCheck("estado_vehiculo", "Estado del vehículo", "ok", "stub", "ACTIVO")], []);
        var handler = HandlerWith(("kyverum_runt", new StubProvider("kyverum_runt", activo)));

        var (result, error, _, vehicleState) = await handler.HandleAsync(MatriculaRequest(tenant), TestContext.Current.CancellationToken);

        error.Should().Be(VehicleStatePolicy.ErrorCode);
        vehicleState!.VehicleStatus.Should().Be(VehicleStatePolicy.VehicleStatusActivoRunt);
        result.Should().BeNull();
    }

    [Fact]
    public async Task Preview_SinIdentificador_NoConsultaProveedor()
    {
        var provider = new StubProvider("kyverum_runt", VehiculoOk());
        var handler = HandlerWith(("kyverum_runt", provider));

        var (_, error, _, _) = await handler.HandleAsync(
            new PreflightPreviewRequest(Guid.NewGuid(), TramiteModalidadEntradaCodes.MatriculaInicial, null, null, null, null),
            TestContext.Current.CancellationToken);

        error.Should().Be("identificador_requerido");
        provider.Calls.Should().Be(0);
    }

    // ── Reúso de la consulta al crear el trámite (AC5) ────────────────────────

    /// <summary>
    /// AC5 — al crear el trámite en el paso 2 el preflight autoritativo corre completo sobre la
    /// instancia (compone y persiste el snapshot), pero NO vuelve a llamar al proveedor externo:
    /// reusa la consulta que ya resolvió el paso 1.
    /// </summary>
    [Fact]
    public async Task Preflight_ConConsultaDelPaso1_NoVuelveAConsultarAlProveedor()
    {
        var tenant = Guid.NewGuid();
        var instance = new Flit.Tramites.Domain.Entities.ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteModalidadEntradaCodes.MatriculaInicial),
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "MAT-2026-000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new Flit.Tramites.Domain.Entities.ProcedureInstanceFieldValue
        {
            FieldKey = "vin",
            ValueText = VinLibre,
            Source = "user",
        });

        _repo.GetByIdWithWizardGraphAsync(instance.Id, tenant, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.FindTramitesByVinAsync(tenant, Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var provider = new StubProvider("kyverum_runt", VehiculoOk());
        var registry = new StaticRegistry(new Dictionary<string, IConsultationProvider> { ["kyverum_runt"] = provider });
        var handler = new RunPreflightHandler(
            _repo,
            registry,
            new ConsultationProviderChainResolver(registry, new ConsultationChainOptions()),
            new NullOverrideProvider(),
            NullConsultationRestrictionPolicy.Instance,
            NullTransitOfficeResolver.Instance,
            NullConsultationBlockingPolicy.Instance);

        // Set representativo de lo que hidrata una consulta RUNT real (marca, línea, modelo, color,
        // combustible, motor…): TODO debe terminar persistido en la instancia, no solo lo que se pinta.
        var hidratados = new[]
        {
            new HydratedField("vehicle_brand", "RENAULT", null),
            new HydratedField("vehicle_line", "LOGAN", null),
            new HydratedField("vehicle_model", "2026", null),
            new HydratedField("vehicle_color", "BLANCO", null),
            new HydratedField("vehicle_fuel", "GASOLINA", null),
            new HydratedField("vehicle_engine", "K7MA812", null),
            new HydratedField("vehicle_status", "REGISTRADO", null),
        };

        var precomputed = new PreflightVehicleSnapshot(
            [new PreflightCheckDto("estado_vehiculo", "Estado del vehículo", "warn", "kyverum_runt", "REGISTRADO")],
            hidratados,
            ["kyverum_runt"]);

        var (result, error, _, _) = await handler.HandleAsync(
            instance.Id, tenant, precomputed, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        provider.Calls.Should().Be(0);
        result!.Checks.Should().ContainSingle(c => c.Key == "estado_vehiculo");
        result.Provider.Should().Be("kyverum_runt");

        // El snapshot SÍ se persiste sobre la instancia real (el reúso solo evita la consulta externa).
        await _repo.Received(1).AddPreflightSnapshotAsync(
            Arg.Any<Flit.Tramites.Domain.Entities.ProcedureInstancePreflightSnapshot>(),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // Paridad con el flujo anterior: los atributos consultados quedan en field_values con
        // Source="consultation", igual que cuando el preflight corría con el trámite ya creado.
        foreach (var campo in hidratados)
        {
            instance.FieldValues.Should().Contain(
                f => f.FieldKey == campo.FieldKey
                     && f.ValueText == campo.ValueText
                     && f.Source == "consultation",
                because: $"la consulta del paso 1 debe persistir {campo.FieldKey} al llegar al paso 2");
        }

        // A4/B4 (HU #10673) — color/combustible además guardan su snapshot RUNT ("{key}_runt"),
        // que es lo que alimenta la detección de transformaciones declaradas.
        instance.FieldValues.Should().Contain(f => f.FieldKey == "vehicle_color_runt" && f.ValueText == "BLANCO");
        instance.FieldValues.Should().Contain(f => f.FieldKey == "vehicle_fuel_runt" && f.ValueText == "GASOLINA");
    }

    // ── Store de consultas del paso 1 ─────────────────────────────────────────

    [Fact]
    public void Store_DevuelveLaConsultaUnaSolaVez()
    {
        var tenant = Guid.NewGuid();
        var snapshot = new PreflightVehicleSnapshot([], [], ["kyverum_runt"]);

        var token = _store.Save(tenant, snapshot);

        _store.TryTake(tenant, token).Should().BeSameAs(snapshot);
        // One-shot: un segundo intento (p. ej. doble clic en "Continuar") ya no reusa la consulta.
        _store.TryTake(tenant, token).Should().BeNull();
    }

    [Fact]
    public void Store_NoEntregaLaConsultaAOtroTenant()
    {
        var token = _store.Save(Guid.NewGuid(), new PreflightVehicleSnapshot([], [], []));

        _store.TryTake(Guid.NewGuid(), token).Should().BeNull();
    }

    [Fact]
    public void Store_TokenDesconocidoDegradaANull()
    {
        _store.TryTake(Guid.NewGuid(), "no-existe").Should().BeNull();
        _store.TryTake(Guid.NewGuid(), null).Should().BeNull();
    }
}
