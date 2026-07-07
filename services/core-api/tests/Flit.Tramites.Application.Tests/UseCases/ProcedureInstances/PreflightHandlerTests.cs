using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class PreflightHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    // ── Test doubles para providers ───────────────────────────────────────────

    private sealed class StubProvider(string key, ConsultationResult result) : IConsultationProvider
    {
        public string Key => key;
        // Provider = Key para que el tracking de providersUsed (que usa result.Provider) case con la
        // key registrada, igual que los providers reales (kyverum_runt/verifik).
        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct) =>
            Task.FromResult(result with { Provider = key });
    }

    private sealed class NullOverrideProvider : IConsultationTenantOverrideProvider
    {
        public Task<ConsultationTenantOverride?> GetAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult<ConsultationTenantOverride?>(null);
    }

    private sealed class FixedOverrideProvider(ConsultationTenantOverride? value) : IConsultationTenantOverrideProvider
    {
        public Task<ConsultationTenantOverride?> GetAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult(value);
    }

    private sealed class ThrowingProvider(string key) : IConsultationProvider
    {
        public string Key => key;
        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }

    /// <summary>Captura el contexto recibido para aserciones sobre los field_values pasados al provider.</summary>
    private sealed class CapturingProvider(string key, ConsultationResult result) : IConsultationProvider
    {
        public string Key => key;
        public ConsultationContext? LastContext { get; private set; }
        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            LastContext = ctx;
            return Task.FromResult(result);
        }
    }

    private sealed class StaticRegistry(Dictionary<string, IConsultationProvider> providers) : IConsultationProviderRegistry
    {
        public IConsultationProvider? Resolve(string providerKey) =>
            providers.TryGetValue(providerKey, out var p) ? p : null;
    }

    private static ConsultationResult Result(string overall, params ConsultationCheck[] checks) =>
        new("stub", overall, checks, []);

    private static ConsultationCheck Check(string status) =>
        new("vehiculo", "Vehículo RUNT", status, "stub", null);

    private static ProcedureInstance Instance(
        string modalidad,
        string status = TramiteEstado.Borrador,
        params ProcedureInstanceActor[] actors)
    {
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            ModalidadEntrada = modalidad,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        foreach (var a in actors)
            instance.Actors.Add(a);
        return instance;
    }

    private static ProcedureInstanceActor Actor(string actorType, string doc = "123") =>
        new()
        {
            ActorType = actorType,
            DocumentType = "CC",
            DocumentNumber = doc,
            FullName = "X",
            Email = "x@x.com",
        };

    private RunPreflightHandler HandlerWith(params (string key, IConsultationProvider provider)[] providers) =>
        BuildHandler(null, providers);

    private RunPreflightHandler HandlerWith(
        ConsultationTenantOverride tenantOverride,
        params (string key, IConsultationProvider provider)[] providers) =>
        BuildHandler(tenantOverride, providers);

    private RunPreflightHandler BuildHandler(
        ConsultationTenantOverride? tenantOverride,
        (string key, IConsultationProvider provider)[] providers)
    {
        var dict = providers.ToDictionary(p => p.key, p => p.provider);
        var registry = new StaticRegistry(dict);
        var resolver = new ConsultationProviderChainResolver(registry, new ConsultationChainOptions());
        IConsultationTenantOverrideProvider overrideProvider = tenantOverride is null
            ? new NullOverrideProvider()
            : new FixedOverrideProvider(tenantOverride);
        return new RunPreflightHandler(_repo, registry, resolver, overrideProvider);
    }

    // ── 404 / 409 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_InstanceNotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);
        var handler = HandlerWith();

        var (result, error) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Post_NotDraft_ReturnsConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", status: "submitted");
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(("verifik", new StubProvider("verifik", Result("green", Check("ok")))));

        var (_, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().Be("not_draft");
    }

    // ── Composición de overall (regla del dominio) ────────────────────────────

    [Fact]
    public void ComposeOverall_AnyFail_IsRed()
    {
        RunPreflightHandler.ComposeOverall(
        [
            new PreflightCheckDto("a", "A", "ok", "s", null),
            new PreflightCheckDto("b", "B", "fail", "s", null),
            new PreflightCheckDto("c", "C", "warn", "s", null),
        ]).Should().Be("red");
    }

    [Fact]
    public void ComposeOverall_WarnNoFail_IsYellow()
    {
        RunPreflightHandler.ComposeOverall(
        [
            new PreflightCheckDto("a", "A", "ok", "s", null),
            new PreflightCheckDto("b", "B", "warn", "s", null),
        ]).Should().Be("yellow");
    }

    [Fact]
    public void ComposeOverall_UnknownDoesNotBlockGreen()
    {
        RunPreflightHandler.ComposeOverall(
        [
            new PreflightCheckDto("a", "A", "ok", "s", null),
            new PreflightCheckDto("b", "B", "unknown", "s", null),
        ]).Should().Be("green");
    }

    [Fact]
    public void ComposeOverall_AnyError_IsRed()
    {
        // "error" (proveedor no verificable) pinta red igual que fail.
        RunPreflightHandler.ComposeOverall(
        [
            new PreflightCheckDto("a", "A", "ok", "s", null),
            new PreflightCheckDto("b", "B", "error", "s", null),
        ]).Should().Be("red");
    }

    [Fact]
    public void ComposeOverall_NoChecks_IsGreen() =>
        RunPreflightHandler.ComposeOverall([]).Should().Be("green");

    // ── Providers por modalidad ───────────────────────────────────────────────

    [Fact]
    public async Task Post_Matricula_RunsOnlyVehiculo()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(
            ("verifik", new StubProvider("verifik", Result("green", Check("ok")))),
            ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))));

        var (result, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Provider.Should().Be("verifik"); // SIMIT no corre en matrícula.
        result.Overall.Should().Be("green");
    }

    // ── HU #10478: cadena Kyverum-first → Verifik ─────────────────────────────

    [Fact]
    public async Task Post_Matricula_KyverumFirst_UsaKyverumCuandoEstaRegistrado()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(
            ("kyverum_runt", new StubProvider("kyverum_runt", Result("green", Check("ok")))),
            ("verifik", new StubProvider("verifik", Result("green", Check("ok")))));

        var (result, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Provider.Should().Be("kyverum_runt"); // Kyverum-first: gana al fallback Verifik.
    }

    [Fact]
    public async Task Post_Matricula_KyverumNoVerificable_CaeAVerifik()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(
            ("kyverum_runt", new StubProvider("kyverum_runt", Result("red", Check("error")))),
            ("verifik", new StubProvider("verifik", Result("green", Check("ok")))));

        var (result, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Provider.Should().Be("verifik"); // fallback al proveedor de contingencia.
        result.Overall.Should().Be("green");
    }

    [Fact]
    public async Task Post_Matricula_OverrideTenantVerifikFirst_IgnoraDefaultKyverum()
    {
        // Glue AC3: config persistida en tenant → preflight respeta primary verifik aunque el default global sea Kyverum-first.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var tenantOverride = new ConsultationTenantOverride(
            new Dictionary<string, ConsultationChainSelection>(StringComparer.OrdinalIgnoreCase)
            {
                [ConsultationKindKeys.VehicleVin] = new("verifik", ["kyverum_runt"]),
            },
            FailoverTimeoutMs: 6000);
        var kyverum = new StubProvider("kyverum_runt", Result("green", Check("ok")));
        var verifik = new StubProvider("verifik", Result("green", Check("ok")));
        var handler = HandlerWith(tenantOverride, ("kyverum_runt", kyverum), ("verifik", verifik));

        var (result, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Provider.Should().Be("verifik");
    }

    // ── Estado del vehículo: REGISTRADO no bloquea matrícula inicial ───────────

    private static ConsultationCheck EstadoVehiculoCheck(string status) =>
        new("estado_vehiculo", "Estado del vehículo", status, "stub", "Estado: REGISTRADO");

    [Fact]
    public async Task Post_Matricula_EstadoVehiculoFail_DegradaAWarnNoRed()
    {
        // En matrícula inicial el estado RUNT "REGISTRADO" (fail) es el esperado para un 0 km:
        // se degrada a warn (amarillo, informativo) y NO pinta el preflight en rojo.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(
            ("verifik", new StubProvider("verifik", Result("red", EstadoVehiculoCheck("fail")))));

        var (result, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Overall.Should().Be("yellow");
        result.Checks.Should().ContainSingle(c => c.Key == "estado_vehiculo" && c.Status == "warn");
    }

    [Fact]
    public async Task Post_Traspaso_EstadoVehiculoFail_SiguePintandoRed()
    {
        // En traspaso se exige vehículo ACTIVO (en circulación): el fail se mantiene → rojo.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: [Actor("comprador", "111"), Actor("vendedor", "222")]);
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(
            ("verifik", new StubProvider("verifik", Result("red", EstadoVehiculoCheck("fail")))),
            ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))),
            ("verifik_rnmc", new StubProvider("verifik_rnmc", Result("green", Check("ok")))));

        var (result, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Overall.Should().Be("red");
        result.Checks.Should().ContainSingle(c => c.Key == "estado_vehiculo" && c.Status == "fail");
    }

    [Fact]
    public async Task Post_Traspaso_RunsVehiculoSimitBothAndRnmc()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: [Actor("comprador", "111"), Actor("vendedor", "222")]);
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(
            ("verifik", new StubProvider("verifik", Result("green", Check("ok")))),
            ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))),
            ("verifik_rnmc", new StubProvider("verifik_rnmc", Result("green", Check("ok")))));

        var (result, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Provider.Should().Contain("verifik");
        result.Provider.Should().Contain("verifik_simit");
        result.Provider.Should().Contain("verifik_rnmc");
        // vehiculo + simit comprador + simit vendedor + rnmc = 4 checks.
        result.Checks.Should().HaveCount(4);
    }

    [Fact]
    public async Task Post_Traspaso_VehiculoContextIncludesOwnerDocumentFromFieldValues()
    {
        var ct = TestContext.Current.CancellationToken;
        // Traspaso con el doc del propietario en field_values (paso "consulta"),
        // sin actor vendedor todavía.
        var instance = Instance("traspaso", actors: Actor("comprador", "111"));
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "owner_document_type", ValueText = "CC", Source = "user" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "owner_document_number", ValueText = "987654", Source = "user" });
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var vehiculo = new CapturingProvider("verifik", Result("green", Check("ok")));
        var handler = HandlerWith(
            ("verifik", vehiculo),
            ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))),
            ("verifik_rnmc", new StubProvider("verifik_rnmc", Result("green", Check("ok")))));

        var (_, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        vehiculo.LastContext.Should().NotBeNull();
        vehiculo.LastContext!.FieldValues.Should().Contain("plate", "ABC123");
        vehiculo.LastContext.FieldValues.Should().Contain("owner_document_type", "CC");
        vehiculo.LastContext.FieldValues.Should().Contain("owner_document_number", "987654");
    }

    // ── Persistencia ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_PersistsSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(("verifik", new StubProvider("verifik", Result("red", Check("fail")))));

        var (result, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        result!.Overall.Should().Be("red");
        await _repo.Received(1).AddPreflightSnapshotAsync(
            Arg.Is<ProcedureInstancePreflightSnapshot>(s =>
                s.Overall == "red" &&
                s.ProcedureInstanceId == instance.Id &&
                s.TenantId == instance.TenantId &&
                s.Checks.Contains("fail")),
            ct);
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    // ── Fallo de proveedor: no 500, pero check error → bloqueo duro (red) ──────

    [Fact]
    public async Task Post_ProviderThrows_ProducesErrorCheckRedNoThrow()
    {
        // La consulta es vital: una excepción inesperada del provider NO se degrada a unknown; se
        // traduce a un check "error" (rojo, bloqueo duro) sin propagar 500 al caller.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(("verifik", new ThrowingProvider("verifik")));

        var (result, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Overall.Should().Be("red"); // error bloquea (dato vital no verificable).
        result.Checks.Should().ContainSingle(c => c.Status == "error");
    }

    [Fact]
    public async Task Post_ProviderNotRegistered_ProducesErrorCheckRed()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(); // registry vacío → no se puede verificar.

        var (result, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Checks.Should().ContainSingle(c => c.Status == "error");
        result.Overall.Should().Be("red");
    }

    // ── HU #10538 (R3): VIN ya matriculado → check informativo + señal de traspaso ─────────────

    private RunPreflightHandler VehiculoOkHandler() =>
        HandlerWith(("verifik", new StubProvider("verifik", Result("green", Check("ok")))));

    [Fact]
    public async Task Post_Matricula_VinYaMatriculado_AgregaCheckInformativoYSenalTraspaso()
    {
        // AC1: un VIN con matrícula previa registrada en el mismo tenant → check informativo
        // (warn) con secretaría + fecha, y señal vin_conflicto_traspaso = true en field_values.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        _repo.FindTramitesByVinAsync(instance.TenantId, Arg.Any<string>(), instance.Id, ct)
            .Returns(new List<VinTramiteExistente>
            {
                new(Guid.NewGuid(), TramiteEstado.Aprobado, Paso: 5, Placa: "XYZ789",
                    Vin: "1HGCM82633A004352", Secretaria: "Secretaría de Movilidad de Bogotá",
                    FechaRegistro: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero)),
            });

        var (result, error) = await VehiculoOkHandler().HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        var check = result!.Checks.Should().ContainSingle(c => c.Key == "vin_matricula").Subject;
        check.Status.Should().Be("warn");
        check.Message.Should().Contain("Secretaría de Movilidad de Bogotá").And.Contain("2026-01-15");
        result.Overall.Should().Be("yellow"); // informativo: nunca bloquea en rojo.
        instance.FieldValues.Should().ContainSingle(f =>
            f.FieldKey == "vin_conflicto_traspaso" && f.ValueText == "true" && f.Source == "system");
    }

    [Fact]
    public async Task Post_Matricula_UnicaPreviaRechazada_NoMarcaConflicto()
    {
        // AC2: si la única matrícula previa del VIN está rechazada, no hay conflicto (se permite
        // reintentar) → sin check vin_matricula ni señal de traspaso.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        _repo.FindTramitesByVinAsync(instance.TenantId, Arg.Any<string>(), instance.Id, ct)
            .Returns(new List<VinTramiteExistente>
            {
                new(Guid.NewGuid(), TramiteEstado.Rechazado, Paso: 3, Placa: null,
                    Vin: "1HGCM82633A004352"),
            });

        var (result, error) = await VehiculoOkHandler().HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Checks.Should().NotContain(c => c.Key == "vin_matricula");
        result.Overall.Should().Be("green");
        instance.FieldValues.Should().NotContain(f => f.FieldKey == "vin_conflicto_traspaso");
    }

    [Fact]
    public async Task Post_Matricula_SinMatriculaPrevia_NoAgregaCheck()
    {
        // AC3: un VIN sin matrículas previas → no se agrega el check de conflicto ni la señal.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        _repo.FindTramitesByVinAsync(instance.TenantId, Arg.Any<string>(), instance.Id, ct)
            .Returns(new List<VinTramiteExistente>());

        var (result, error) = await VehiculoOkHandler().HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Checks.Should().NotContain(c => c.Key == "vin_matricula");
        result.Overall.Should().Be("green");
        instance.FieldValues.Should().NotContain(f => f.FieldKey == "vin_conflicto_traspaso");
    }
}
