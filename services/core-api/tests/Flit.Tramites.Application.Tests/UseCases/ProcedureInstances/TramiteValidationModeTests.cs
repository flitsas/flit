using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #10970 — modo por ambiente de CF-01 (duplicidad de trámite en curso) y CF-03 (precondición
/// registral): <c>block</c> (default fail-safe) / <c>warn</c> / <c>off</c>.
/// </summary>
public sealed class TramiteValidationModeTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    // ── Doubles mínimos (mismo patrón que PreflightHandlerTests) ──────────────

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

    private static ConsultationResult Result(string overall, params ConsultationCheck[] checks) =>
        new("stub", overall, checks, []);

    /// <summary>Check del vehículo con la key que el mapper convierte en <c>estado_vehiculo</c>.</summary>
    private static ConsultationCheck EstadoVehiculo(string status) =>
        new("estado_vehiculo", "Estado del vehículo", status, "stub", null);

    private static ProcedureInstance Instance(string modalidad)
    {
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = modalidad,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        instance.Actors.Add(new ProcedureInstanceActor
        {
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = "123",
            FullName = "X",
            Email = "x@x.com",
        });
        return instance;
    }

    private RunPreflightHandler Handler(TramiteValidationPolicy? policy, ConsultationCheck? vehiculo = null)
    {
        var providers = new Dictionary<string, IConsultationProvider>
        {
            ["verifik"] = new StubProvider("verifik", Result("green", vehiculo ?? EstadoVehiculo("fail"))),
            ["verifik_simit"] = new StubProvider("verifik_simit", Result("green")),
            ["verifik_rnmc"] = new StubProvider("verifik_rnmc", Result("green")),
        };
        var registry = new StaticRegistry(providers);
        return new RunPreflightHandler(
            _repo,
            registry,
            new ConsultationProviderChainResolver(registry, new ConsultationChainOptions()),
            new NullOverrideProvider(),
            NullConsultationRestrictionPolicy.Instance,
            NullTransitOfficeResolver.Instance,
            NullConsultationBlockingPolicy.Instance,
            policy);
    }

    private static TramiteValidationPolicy Policy(
        TramiteValidationMode duplicidad = TramiteValidationMode.Block,
        TramiteValidationMode registral = TramiteValidationMode.Block) =>
        new(duplicidad, registral);

    // ── AC6 / AC7: resolución de la configuración ─────────────────────────────

    [Fact]
    public void Resolve_SinConfiguracion_AmbasValidacionesEnBlock()
    {
        // AC6 — fail-safe: un ambiente sin las variables definidas NO se relaja.
        var policy = TramiteValidationPolicy.Resolve(new TramiteValidationPolicyOptions());

        policy.DuplicateActiveProcedure.Should().Be(TramiteValidationMode.Block);
        policy.VehicleRegistrationState.Should().Be(TramiteValidationMode.Block);
    }

    [Theory]
    [InlineData("block", TramiteValidationMode.Block)]
    [InlineData("warn", TramiteValidationMode.Warn)]
    [InlineData("off", TramiteValidationMode.Off)]
    [InlineData("  WARN  ", TramiteValidationMode.Warn)]
    [InlineData("Off", TramiteValidationMode.Off)]
    public void Resolve_ValorValido_MapeaElModo(string raw, TramiteValidationMode esperado)
    {
        // El valor del .env llega como texto libre: se admite con espacios y sin importar el case.
        var options = new TramiteValidationPolicyOptions
        {
            DuplicateActiveProcedure = new TramiteValidationSetting { Mode = raw },
            VehicleRegistrationState = new TramiteValidationSetting { Mode = raw },
        };

        var policy = TramiteValidationPolicy.Resolve(options);

        policy.DuplicateActiveProcedure.Should().Be(esperado);
        policy.VehicleRegistrationState.Should().Be(esperado);
    }

    [Fact]
    public void Resolve_ValorNoReconocido_CaeABlockYAvisa()
    {
        // AC7 — un typo en el .env ("desactivado", "false", …) NO puede apagar la regla en producción:
        // resuelve a block y deja constancia para el log de arranque.
        var avisos = new List<(string Validacion, string? Valor)>();
        var options = new TramiteValidationPolicyOptions
        {
            VehicleRegistrationState = new TramiteValidationSetting { Mode = "desactivado" },
        };

        var policy = TramiteValidationPolicy.Resolve(options, (name, raw) => avisos.Add((name, raw)));

        policy.VehicleRegistrationState.Should().Be(TramiteValidationMode.Block);
        avisos.Should().ContainSingle();
        avisos[0].Validacion.Should().Be(nameof(TramiteValidationPolicyOptions.VehicleRegistrationState));
        avisos[0].Valor.Should().Be("desactivado");
    }

    [Fact]
    public void Resolve_ValorAusente_NoGeneraAviso()
    {
        // Ausente es el DEFAULT documentado, no un error: no debe ensuciar el log de arranque.
        var avisos = new List<(string, string?)>();

        TramiteValidationPolicy.Resolve(new TramiteValidationPolicyOptions(), (n, r) => avisos.Add((n, r)));

        avisos.Should().BeEmpty();
    }

    [Fact]
    public void BlockAll_EsElDefaultDeLosHandlers()
    {
        TramiteValidationPolicy.BlockAll.DuplicateActiveProcedure.Should().Be(TramiteValidationMode.Block);
        TramiteValidationPolicy.BlockAll.VehicleRegistrationState.Should().Be(TramiteValidationMode.Block);
    }

    // ── AC1 / AC2 / AC3: CF-01 duplicidad ─────────────────────────────────────

    [Fact]
    public async Task Duplicidad_ModoBlock_Devuelve409YNoPersiste()
    {
        // AC1 — comportamiento actual intacto.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial");
        var existingId = Guid.NewGuid();
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        _repo.FindTramitesByVinAsync(instance.TenantId, Arg.Any<string>(), instance.Id, ct)
            .Returns(new List<VinTramiteExistente>
            {
                new(existingId, TramiteEstado.Borrador, Paso: 2, Placa: null, Vin: "1HGCM82633A004352"),
            });

        var (result, error, existing, _) = await Handler(Policy())
            .HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().Be("DUPLICATE_ACTIVE_PROCEDURE");
        result.Should().BeNull();
        existing.Should().Be(existingId);
        await _repo.DidNotReceive().AddPreflightSnapshotAsync(Arg.Any<ProcedureInstancePreflightSnapshot>(), ct);
    }

    [Fact]
    public async Task Duplicidad_ModoWarn_NoBloqueaYDejaCheckAmarillo()
    {
        // AC2 — el trámite avanza, el snapshot SÍ se persiste y el hallazgo queda visible en amarillo
        // con el id del trámite existente para poder retomarlo a mano.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial");
        var existingId = Guid.NewGuid();
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        _repo.FindTramitesByVinAsync(instance.TenantId, Arg.Any<string>(), instance.Id, ct)
            .Returns(new List<VinTramiteExistente>
            {
                new(existingId, TramiteEstado.Borrador, Paso: 2, Placa: null, Vin: "1HGCM82633A004352"),
            });

        var (result, error, existing, _) =
            await Handler(Policy(duplicidad: TramiteValidationMode.Warn))
                .HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        existing.Should().BeNull();
        result.Should().NotBeNull();
        var check = result!.Checks.Should().ContainSingle(c => c.Key == "duplicidad_tramite").Subject;
        check.Status.Should().Be("warn");
        check.Message.Should().Contain(existingId.ToString());
        result.Overall.Should().Be("yellow");
        await _repo.Received(1).AddPreflightSnapshotAsync(Arg.Any<ProcedureInstancePreflightSnapshot>(), ct);
    }

    [Fact]
    public async Task Duplicidad_ModoOff_NiSiquieraConsultaElRepositorio()
    {
        // AC3 — off no evalúa: sin 409, sin check y sin el viaje a BD por la llave de la familia.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso");
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        _repo.FindTramitesByPlacaAsync(instance.TenantId, Arg.Any<string>(), instance.Id, ct)
            .Returns(new List<PlacaTramiteExistente> { new(Guid.NewGuid(), TramiteEstado.Preparado, Placa: "ABC123") });

        var (result, error, _, _) =
            await Handler(Policy(duplicidad: TramiteValidationMode.Off))
                .HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Checks.Should().NotContain(c => c.Key == "duplicidad_tramite");
        await _repo.DidNotReceive().FindTramitesByPlacaAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), ct);
    }

    // ── AC4: CF-03 precondición registral (fuente RUNT) ───────────────────────

    [Fact]
    public async Task Registral_ModoBlock_VehiculoActivoEnRunt_Devuelve422()
    {
        // Línea base del modo block (HU #10877 AC1): estado_vehiculo "ok" = ya matriculado → 422.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial");
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        _repo.FindTramitesByVinAsync(instance.TenantId, Arg.Any<string>(), instance.Id, ct)
            .Returns(new List<VinTramiteExistente>());

        var (result, error, _, vehicleState) =
            await Handler(Policy(), EstadoVehiculo("ok")).HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().Be(VehicleStatePolicy.ErrorCode);
        result.Should().BeNull();
        vehicleState!.VehicleStatus.Should().Be(VehicleStatePolicy.VehicleStatusActivoRunt);
    }

    [Fact]
    public async Task Registral_ModoWarn_VehiculoActivoEnRunt_NoBloqueaYQuedaAmarillo()
    {
        // AC4 — sin 422: el check baja a warn conservando el mensaje, y el semáforo queda amarillo.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial");
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        _repo.FindTramitesByVinAsync(instance.TenantId, Arg.Any<string>(), instance.Id, ct)
            .Returns(new List<VinTramiteExistente>());

        var (result, error, _, vehicleState) =
            await Handler(Policy(registral: TramiteValidationMode.Warn), EstadoVehiculo("ok"))
                .HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        vehicleState.Should().BeNull();
        var check = result!.Checks.Should().ContainSingle(c => c.Key == "estado_vehiculo").Subject;
        check.Status.Should().Be("warn");
        check.Message.Should().Contain("ya se encuentra matriculado");
        result.Overall.Should().Be("yellow");
    }

    [Fact]
    public async Task Registral_ModoOff_VehiculoActivoEnRunt_NoBloqueaNiDejaSenal()
    {
        // off = la validación no corre: el check conserva el "ok" del proveedor y el semáforo, verde.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial");
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        _repo.FindTramitesByVinAsync(instance.TenantId, Arg.Any<string>(), instance.Id, ct)
            .Returns(new List<VinTramiteExistente>());

        var (result, error, _, _) =
            await Handler(Policy(registral: TramiteValidationMode.Off), EstadoVehiculo("ok"))
                .HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Checks.Should().ContainSingle(c => c.Key == "estado_vehiculo")
            .Which.Status.Should().Be("ok");
        result.Overall.Should().Be("green");
    }

    [Fact]
    public async Task Registral_ModoWarn_RuntSinDato_NoBloquea()
    {
        // AC3 de la HU #10877 (dato no verificable) también deja de bloquear fuera de modo block.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial");
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        _repo.FindTramitesByVinAsync(instance.TenantId, Arg.Any<string>(), instance.Id, ct)
            .Returns(new List<VinTramiteExistente>());

        var (result, error, _, _) =
            await Handler(Policy(registral: TramiteValidationMode.Warn), EstadoVehiculo("unknown"))
                .HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Checks.Should().ContainSingle(c => c.Key == "estado_vehiculo")
            .Which.Status.Should().Be("warn");
    }

    // ── CF-03 fuente FLIT (VIN con matrícula APROBADA) ────────────────────────

    [Fact]
    public async Task Registral_ModoWarn_VinAprobadoEnFlit_DegradaAlCheckInformativo()
    {
        // Fuera de modo block, el VIN con matrícula APROBADA vuelve al check informativo de la
        // HU #10538 ("VIN ya matriculado", warn) con su señal de ruta de traspaso, en vez de 422.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial");
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        _repo.FindTramitesByVinAsync(instance.TenantId, Arg.Any<string>(), instance.Id, ct)
            .Returns(new List<VinTramiteExistente>
            {
                new(Guid.NewGuid(), TramiteEstado.Aprobado, Paso: 5, Placa: "XYZ789",
                    Vin: "1HGCM82633A004352", Secretaria: "Secretaría de Movilidad de Bogotá",
                    FechaRegistro: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero)),
            });

        var (result, error, _, vehicleState) =
            await Handler(Policy(registral: TramiteValidationMode.Warn))
                .HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        vehicleState.Should().BeNull();
        result!.Checks.Should().ContainSingle(c => c.Key == "vin_matricula")
            .Which.Status.Should().Be("warn");
        instance.FieldValues.Should().Contain(f => f.FieldKey == "vin_conflicto_traspaso" && f.ValueText == "true");
    }

    // ── CF-01 en el avance del paso 1 al paso 2 (creación diferida, CF-02) ────

    /// <summary>HU #11199 — la matrícula inicial exige secretaría; el resolver la acepta siempre.</summary>
    private static readonly Guid SecretariaHabilitada = Guid.Parse("22222222-2222-2222-2222-222222222199");

    private sealed class AnyOfficeResolver : ITransitOfficeResolver
    {
        public Task<ResolvedTransitOffice?> ResolveEnabledByNameAsync(
            Guid tenantId, string transitOfficeName, CancellationToken ct = default) =>
            Task.FromResult<ResolvedTransitOffice?>(null);

        public Task<ResolvedTransitOffice?> ResolveEnabledByIdAsync(
            Guid tenantId, Guid transitOfficeId, CancellationToken ct = default) =>
            Task.FromResult<ResolvedTransitOffice?>(
                new ResolvedTransitOffice(transitOfficeId, "05001000", "Secretaría de Medellín", "05001"));
    }

    private CreateProcedureInstanceFromConsultaHandler FromConsultaHandler(TramiteValidationPolicy policy) =>
        new(_repo,
            new CreateProcedureInstanceHandler(_repo, Substitute.For<IProcedureTypeRepository>()),
            new PatchFieldValuesHandler(_repo),
            Handler(policy),
            Substitute.For<IPreflightPreviewStore>(),
            new AnyOfficeResolver(),
            policy);

    private static CreateFromConsultaRequest FromConsultaRequest() =>
        new(TenantId: Guid.NewGuid(),
            CreatedByUserId: Guid.NewGuid(),
            Modalidad: "matricula_inicial",
            Vin: "1HGCM82633A004352",
            Plate: null,
            OwnerDocumentType: null,
            OwnerDocumentNumber: null,
            PreviewToken: null,
            TransitOfficeId: SecretariaHabilitada);

    [Fact]
    public async Task CreateFromConsulta_ModoBlock_DuplicadoDevuelve409SinCrear()
    {
        // El avance paso 1 → paso 2 re-verifica la duplicidad ANTES de persistir nada (CF-02): en modo
        // block sigue cortando ahí, sin dejar un trámite inservible.
        var ct = TestContext.Current.CancellationToken;
        var request = FromConsultaRequest();
        var existingId = Guid.NewGuid();
        _repo.FindTramitesByVinAsync(request.TenantId, Arg.Any<string>(), Guid.Empty, ct)
            .Returns(new List<VinTramiteExistente>
            {
                new(existingId, TramiteEstado.Borrador, Paso: 2, Placa: null, Vin: "1HGCM82633A004352"),
            });

        var (result, error, existing, _) = await FromConsultaHandler(Policy()).HandleAsync(request, ct);

        error.Should().Be("DUPLICATE_ACTIVE_PROCEDURE");
        result.Should().BeNull();
        existing.Should().Be(existingId);
    }

    [Theory]
    [InlineData(TramiteValidationMode.Warn)]
    [InlineData(TramiteValidationMode.Off)]
    public async Task CreateFromConsulta_ModoNoBlock_NoCortaPorDuplicidad(TramiteValidationMode modo)
    {
        // Regresión de la HU #10970: este handler tenía su PROPIO chequeo de CF-01 sin cablear al modo,
        // así que el wizard seguía devolviendo 409 al avanzar de paso aunque el ambiente estuviera en
        // off/warn. Fuera de block no debe cortar aquí: la señal la da el preflight de más abajo.
        var ct = TestContext.Current.CancellationToken;
        var request = FromConsultaRequest();
        _repo.FindTramitesByVinAsync(request.TenantId, Arg.Any<string>(), Guid.Empty, ct)
            .Returns(new List<VinTramiteExistente>
            {
                new(Guid.NewGuid(), TramiteEstado.Borrador, Paso: 2, Placa: null, Vin: "1HGCM82633A004352"),
            });

        var (_, error, existing, _) = await FromConsultaHandler(Policy(duplicidad: modo)).HandleAsync(request, ct);

        error.Should().NotBe("DUPLICATE_ACTIVE_PROCEDURE");
        existing.Should().BeNull();
        // La llave ni se consulta con Guid.Empty (la exclusión que usa este handler antes de crear).
        await _repo.DidNotReceive().FindTramitesByVinAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Is<Guid>(g => g == Guid.Empty), ct);
    }

    // ── Independencia de las dos validaciones ─────────────────────────────────

    [Fact]
    public async Task ModosIndependientes_DuplicidadOffNoApagaElRegistral()
    {
        // Cada validación tiene su propia variable: apagar CF-01 no puede relajar CF-03 (el caso de QA
        // en la matriz acordada es exactamente este, al revés).
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial");
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        _repo.FindTramitesByVinAsync(instance.TenantId, Arg.Any<string>(), instance.Id, ct)
            .Returns(new List<VinTramiteExistente>());

        var (_, error, _, _) = await Handler(
                Policy(duplicidad: TramiteValidationMode.Off, registral: TramiteValidationMode.Block),
                EstadoVehiculo("ok"))
            .HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().Be(VehicleStatePolicy.ErrorCode);
    }
}
