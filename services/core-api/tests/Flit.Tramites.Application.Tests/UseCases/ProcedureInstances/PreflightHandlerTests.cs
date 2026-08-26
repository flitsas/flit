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
using Flit.Tramites.Domain.Integration;

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
            ProcedureType = ProcedureTypeFixture.For(modalidad),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "1HGCM82633A004352", Source = "user" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        foreach (var a in actors)
            instance.Actors.Add(a);
        return instance;
    }

    /// <summary>
    /// ADR-0050 — instancia de un TIPO concreto, para las familias que la sobrecarga por modalidad no
    /// sabe representar (`ProcedureTypeFixture.For` solo distingue matrícula de traspaso).
    /// </summary>
    private static ProcedureInstance InstanceOf(
        ProcedureType type,
        params ProcedureInstanceActor[] actors)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = type,
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = type.Id,
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        // Un trámite de OTROS entra por PLACA: el VIN NO se captura en su paso 1. Dejarlo fuera es
        // parte del caso — si el preflight cayera en la rama de VIN, consultaría con la mano vacía.
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

    private static ProcedureInstanceActor ActorNit(string actorType, string doc) =>
        new()
        {
            ActorType = actorType,
            DocumentType = "NIT",
            DocumentNumber = doc,
            FullName = "EMPRESA X",
            Email = "x@x.com",
        };

    // Check con la key del mapper RNMC ("medidas_correctivas"); con keyPrefix "rnmc_{rol}" queda
    // "rnmc_{rol}_medidas_correctivas" en el snapshot.
    private static ConsultationCheck RnmcCheck(string status) =>
        new("medidas_correctivas", "Medidas correctivas (Policía)", status, "verifik_rnmc", null);

    private RunPreflightHandler HandlerWith(params (string key, IConsultationProvider provider)[] providers) =>
        BuildHandler(null, providers);

    private RunPreflightHandler HandlerWith(
        ConsultationTenantOverride tenantOverride,
        params (string key, IConsultationProvider provider)[] providers) =>
        BuildHandler(tenantOverride, providers);

    private RunPreflightHandler BuildHandler(
        ConsultationTenantOverride? tenantOverride,
        (string key, IConsultationProvider provider)[] providers,
        ITransitOfficeResolver? transitOfficeResolver = null,
        IConsultationRestrictionPolicy? restrictionPolicy = null,
        IConsultationBlockingPolicy? blockingPolicy = null)
    {
        var dict = providers.ToDictionary(p => p.key, p => p.provider);
        var registry = new StaticRegistry(dict);
        var resolver = new ConsultationProviderChainResolver(registry, new ConsultationChainOptions());
        IConsultationTenantOverrideProvider overrideProvider = tenantOverride is null
            ? new NullOverrideProvider()
            : new FixedOverrideProvider(tenantOverride);
        return new RunPreflightHandler(
            _repo, registry, resolver, overrideProvider,
            // Permisivo por defecto: los tests que no ejercitan HU #10760 no cambian de comportamiento.
            restrictionPolicy ?? NullConsultationRestrictionPolicy.Instance,
            transitOfficeResolver ?? NullTransitOfficeResolver.Instance,
            // FEATURE 05 — permisivo por defecto (defaults por criterio).
            blockingPolicy ?? NullConsultationBlockingPolicy.Instance);
    }

    /// <summary>Reglas de bloqueo fijas para probar la severidad configurable del preflight (FEATURE 05).</summary>
    private sealed class StubBlockingPolicy(params (string criterion, bool blocks)[] overrides) : IConsultationBlockingPolicy
    {
        public Task<ConsultationBlockingRules> GetAsync(
            Guid tenantId, Guid? transitOfficeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ConsultationBlockingRules.From(
                overrides.Select(o => new KeyValuePair<string, bool>(o.criterion, o.blocks))));
    }

    /// <summary>
    /// Restricciones fijas, contando invocaciones para probar que el preflight lee la política UNA
    /// sola vez por corrida (las guardas del fan-out son lookups en memoria, no I/O por consulta).
    /// </summary>
    private sealed class CountingRestrictionPolicy(params string[] disabledKinds) : IConsultationRestrictionPolicy
    {
        public int Calls { get; private set; }

        public Task<ConsultationRestrictions> GetAsync(
            Guid tenantId, Guid? transitOfficeId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(ConsultationRestrictions.From(disabledKinds));
        }
    }

    /// <summary>Restricciones con ajustes EXPLÍCITOS por kind (kind → enabled), para probar el opt-in RNMC.</summary>
    private sealed class SettingRestrictionPolicy(params (string kind, bool enabled)[] settings) : IConsultationRestrictionPolicy
    {
        public Task<ConsultationRestrictions> GetAsync(
            Guid tenantId, Guid? transitOfficeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ConsultationRestrictions.FromSettings(
                settings.Select(s => new KeyValuePair<string, bool>(s.kind, s.enabled))));
    }

    /// <summary>Resolver de OT que devuelve un match fijo (o null) sin tocar catálogo/grants reales.</summary>
    private sealed class StubTransitOfficeResolver(ResolvedTransitOffice? match) : ITransitOfficeResolver
    {
        public string? LastName { get; private set; }

        public Task<ResolvedTransitOffice?> ResolveEnabledByNameAsync(
            Guid tenantId, string transitOfficeName, CancellationToken cancellationToken = default)
        {
            LastName = transitOfficeName;
            return Task.FromResult(match);
        }

        /// <summary>El preflight de la instancia resuelve por nombre; la vía por id no se ejercita aquí.</summary>
        public Task<ResolvedTransitOffice?> ResolveEnabledByIdAsync(
            Guid tenantId, Guid transitOfficeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ResolvedTransitOffice?>(null);
    }

    // ── 404 / 409 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_InstanceNotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);
        var handler = HandlerWith();

        var (result, error, _, _) = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

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

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

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

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Provider.Should().Be("verifik"); // SIMIT no corre en matrícula.
        result.Overall.Should().Be("green");
    }

    // ── B11 (HU #10659): auto-bind del OT desde RUNT en traspaso ──────────────

    [Fact]
    public async Task Post_Traspaso_RuntNameMatchesEnabledOffice_BindsTransitOfficeId()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        // El proveedor de vehículo (cadena de placa) hidrata el nombre del OT desde el RUNT.
        var vehiculo = new StubProvider("kyverum_runt",
            new ConsultationResult("kyverum_runt", "green", [Check("ok")],
                [new HydratedField("transit_office_name", "SDM BOGOTÁ", null)]));

        var officeId = Guid.NewGuid();
        var resolver = new StubTransitOfficeResolver(
            new ResolvedTransitOffice(officeId, "11001000", "SDM BOGOTÁ", "11001"));

        var handler = BuildHandler(
            null,
            [
                ("kyverum_runt", vehiculo),
                ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))),
            ],
            transitOfficeResolver: resolver);

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        resolver.LastName.Should().Be("SDM BOGOTÁ");
        instance.FieldValues.Should().ContainSingle(f =>
            f.FieldKey == "transit_office_id" && f.ValueText == officeId.ToString() && f.Source == "consultation");
        instance.FieldValues.Should().ContainSingle(f =>
            f.FieldKey == "transit_office_code" && f.ValueText == "11001000");
        instance.FieldValues.Should().ContainSingle(f =>
            f.FieldKey == "transit_office_city" && f.ValueText == "11001");
    }

    [Fact]
    public async Task Post_Traspaso_RuntNameNotEnabled_KeepsNameWithoutInventingId()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var vehiculo = new StubProvider("kyverum_runt",
            new ConsultationResult("kyverum_runt", "green", [Check("ok")],
                [new HydratedField("transit_office_name", "OT DESCONOCIDO", null)]));
        var resolver = new StubTransitOfficeResolver(match: null); // ningún OT habilitado coincide.

        var handler = BuildHandler(
            null,
            [
                ("kyverum_runt", vehiculo),
                ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))),
            ],
            transitOfficeResolver: resolver);

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        // Se conserva el nombre RUNT pero NO se inventa un transit_office_id.
        instance.FieldValues.Should().ContainSingle(f =>
            f.FieldKey == "transit_office_name" && f.ValueText == "OT DESCONOCIDO");
        instance.FieldValues.Should().NotContain(f => f.FieldKey == "transit_office_id");
    }

    [Fact]
    public async Task Post_Matricula_DoesNotAutoBindTransitOffice()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var vehiculo = new StubProvider("kyverum_runt",
            new ConsultationResult("kyverum_runt", "green", [Check("ok")],
                [new HydratedField("transit_office_name", "SDM BOGOTÁ", null)]));
        var resolver = new StubTransitOfficeResolver(
            new ResolvedTransitOffice(Guid.NewGuid(), "11001000", "SDM BOGOTÁ", "11001"));

        var handler = BuildHandler(null, [("kyverum_runt", vehiculo)], transitOfficeResolver: resolver);

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        // Matrícula: el operador elige libremente; el preflight NO consulta el resolver ni fija el id.
        resolver.LastName.Should().BeNull();
        instance.FieldValues.Should().NotContain(f => f.FieldKey == "transit_office_id");
    }

    // ── ADR-0050: la familia OTROS entra por PLACA ────────────────────────────
    // Antes caía en el `else` de la rama de matrícula y se consultaba por VIN. Un blindaje o un
    // duplicado de tarjeta nunca traen VIN —su paso 1 pide placa—, así que el pre-vuelo corría
    // contra un identificador vacío y el semáforo salía en gris sin que nada fallara.

    /// <summary>Cadena con un proveedor DISTINTO por identificador, para ver cuál se usó.</summary>
    private static ConsultationTenantOverride CadenaPorIdentificador() =>
        new(
            new Dictionary<string, ConsultationChainSelection>(StringComparer.OrdinalIgnoreCase)
            {
                [ConsultationKindKeys.VehiclePlate] = new("proveedor_placa", []),
                [ConsultationKindKeys.VehicleVin] = new("proveedor_vin", []),
            },
            FailoverTimeoutMs: 6000);

    [Fact]
    public async Task Post_Otros_ConsultaElVehiculoPorPlaca_NoPorVin()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = InstanceOf(ProcedureTypeFixture.Blindaje, Actor("comprador", "111"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var handler = HandlerWith(
            CadenaPorIdentificador(),
            ("proveedor_placa", new StubProvider("proveedor_placa", Result("green", Check("ok")))),
            ("proveedor_vin", new StubProvider("proveedor_vin", Result("green", Check("ok")))),
            ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))));

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Provider.Should().Contain("proveedor_placa");
        result.Provider.Should().NotContain("proveedor_vin");
    }

    // ── Cambio de carrocería sobre un vehículo sin carrocería ────────────────────────────────────

    /// <summary>Preflight de un CAMBIO_CARROCERIA cuyo RUNT devuelve la carrocería indicada.</summary>
    private async Task<string?> PreflightCambioCarroceria(
        CancellationToken ct, params HydratedField[] hidratados)
    {
        var instance = InstanceOf(ProcedureTypeFixture.CambioCarroceria, Actor("comprador", "111"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var handler = HandlerWith(
            CadenaPorIdentificador(),
            ("proveedor_placa", new StubProvider("proveedor_placa",
                new ConsultationResult("proveedor_placa", "green", [Check("ok")], hidratados))),
            ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))));

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);
        return error;
    }

    [Fact]
    public async Task CambioCarroceria_RuntReportaSinCarroceria_Bloquea()
    {
        // El caso real de una motocicleta: el RUNT NO devuelve el campo vacío, devuelve
        // «SIN CARROCERIA». Mirando solo el vacío, el trámite pasaba el pre-vuelo y el gestor llegaba
        // a un paso donde el selector de carrocería nueva no tenía ni una opción que ofrecer.
        var ct = TestContext.Current.CancellationToken;

        var error = await PreflightCambioCarroceria(
            ct,
            new HydratedField("vehicle_class", "MOTOCICLETA", null),
            new HydratedField("vehicle_body_type", "SIN CARROCERIA", null));

        error.Should().Be(VehicleBodyTypePolicy.ErrorCode);
    }

    [Fact]
    public async Task CambioCarroceria_RuntNoTraeElCampo_Bloquea()
    {
        var ct = TestContext.Current.CancellationToken;

        var error = await PreflightCambioCarroceria(
            ct, new HydratedField("vehicle_class", "CAMION", null));

        error.Should().Be(VehicleBodyTypePolicy.ErrorCode);
    }

    [Fact]
    public async Task CambioCarroceria_ConCarroceriaDeVerdad_NoBloquea()
    {
        var ct = TestContext.Current.CancellationToken;

        var error = await PreflightCambioCarroceria(
            ct,
            new HydratedField("vehicle_class", "CAMION", null),
            new HydratedField("vehicle_body_type", "ESTACAS", null));

        error.Should().BeNull();
    }

    // ── Levantamiento de prenda sobre un vehículo sin gravamen ──────────────────────────────────

    /// <summary>Check del semáforo de gravámenes con el estado indicado.</summary>
    private static ConsultationCheck CheckGravamenes(string status) =>
        new("gravamenes", "Gravámenes y limitaciones", status, "stub", null);

    /// <summary>Preflight de un tipo prendario cuyo RUNT devuelve el semáforo de gravámenes dado.</summary>
    private async Task<string?> PreflightPrendario(
        ProcedureType tipo, CancellationToken ct, params ConsultationCheck[] checks)
    {
        var instance = InstanceOf(tipo, Actor("comprador", "111"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var handler = HandlerWith(
            CadenaPorIdentificador(),
            ("proveedor_placa", new StubProvider("proveedor_placa",
                new ConsultationResult("proveedor_placa", "green", checks, []))),
            ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))));

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);
        return error;
    }

    [Fact]
    public async Task Levantamiento_RuntSinGravamen_Bloquea()
    {
        var ct = TestContext.Current.CancellationToken;

        var error = await PreflightPrendario(
            ProcedureTypeFixture.LevantamientoPrenda, ct, Check("ok"), CheckGravamenes("ok"));

        error.Should().Be(VehiclePrendaPolicy.ErrorCode);
    }

    [Fact]
    public async Task Levantamiento_RuntConGravamen_NoBloquea()
    {
        var ct = TestContext.Current.CancellationToken;

        var error = await PreflightPrendario(
            ProcedureTypeFixture.LevantamientoPrenda, ct, Check("ok"), CheckGravamenes("warn"));

        error.Should().BeNull();
    }

    [Fact]
    public async Task Levantamiento_RuntSinInformacionDeGravamenes_NoBloquea()
    {
        // «No se sabe» no es «no tiene»: sin dato del proveedor no se le niega el trámite al gestor.
        var ct = TestContext.Current.CancellationToken;

        var error = await PreflightPrendario(
            ProcedureTypeFixture.LevantamientoPrenda, ct, Check("ok"), CheckGravamenes("unknown"));

        error.Should().BeNull();
    }

    [Fact]
    public async Task Inscripcion_SobreVehiculoSinGravamen_NoBloquea()
    {
        // La inscripción CONSTITUYE el gravamen: presuponerlo sería impedir el trámite justo en su
        // caso normal.
        var ct = TestContext.Current.CancellationToken;

        var error = await PreflightPrendario(
            ProcedureTypeFixture.PrendaInscripcion, ct, Check("ok"), CheckGravamenes("ok"));

        error.Should().BeNull();
    }

    [Fact]
    public async Task OtroTipoDeOtros_SinCarroceria_NoSeBloquea()
    {
        // La carrocería solo es precondición del trámite que la cambia: un blindaje sobre la misma
        // motocicleta tiene que poder radicarse.
        var ct = TestContext.Current.CancellationToken;
        var instance = InstanceOf(ProcedureTypeFixture.Blindaje, Actor("comprador", "111"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var handler = HandlerWith(
            CadenaPorIdentificador(),
            ("proveedor_placa", new StubProvider("proveedor_placa",
                new ConsultationResult("proveedor_placa", "green", [Check("ok")],
                    [new HydratedField("vehicle_body_type", "SIN CARROCERIA", null)]))),
            ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))));

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
    }

    [Fact]
    public async Task Post_Matricula_SigueConsultandoPorVin()
    {
        // REGRESIÓN de la rama que NO se tocó: la matrícula inicial es el único caso sin placa.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var handler = HandlerWith(
            CadenaPorIdentificador(),
            ("proveedor_placa", new StubProvider("proveedor_placa", Result("green", Check("ok")))),
            ("proveedor_vin", new StubProvider("proveedor_vin", Result("green", Check("ok")))));

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Provider.Should().Contain("proveedor_vin");
    }

    [Fact]
    public async Task Post_Otros_ConsultaComparendosDelTitular_YNoDeUnVendedorQueNoInterviene()
    {
        // En OTROS interviene UN solo actor: el propietario inscrito, persistido como `comprador`.
        // El expediente trae también un vendedor (residuo posible de un borrador o de una vía de
        // entrada): no debe consultarse, porque en este trámite nadie vende.
        var ct = TestContext.Current.CancellationToken;
        var instance = InstanceOf(
            ProcedureTypeFixture.Blindaje, Actor("comprador", "111"), Actor("vendedor", "222"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var handler = HandlerWith(
            ("kyverum_runt", new StubProvider("kyverum_runt", Result("green", Check("ok")))),
            ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))));

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Checks.Should().Contain(c => c.Key.StartsWith("simit_comprador", StringComparison.Ordinal));
        result.Checks.Should().NotContain(c => c.Key.StartsWith("simit_vendedor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post_Otros_FijaElOrganismoDesdeElRunt()
    {
        // El vehículo ya está inscrito: el organismo lo fija el registro, no el operador. Estaba
        // atado al código de traspaso_standard, así que la familia OTROS lo elegía a mano y podía
        // escoger uno distinto al del RUNT.
        var ct = TestContext.Current.CancellationToken;
        var instance = InstanceOf(ProcedureTypeFixture.Blindaje, Actor("comprador", "111"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var vehiculo = new StubProvider("kyverum_runt",
            new ConsultationResult("kyverum_runt", "green", [Check("ok")],
                [new HydratedField("transit_office_name", "SDM BOGOTÁ", null)]));
        var officeId = Guid.NewGuid();
        var resolver = new StubTransitOfficeResolver(
            new ResolvedTransitOffice(officeId, "11001000", "SDM BOGOTÁ", "11001"));

        var handler = BuildHandler(
            null,
            [
                ("kyverum_runt", vehiculo),
                ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))),
            ],
            transitOfficeResolver: resolver);

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        resolver.LastName.Should().Be("SDM BOGOTÁ");
        instance.FieldValues.Should().ContainSingle(f =>
            f.FieldKey == "transit_office_id" && f.ValueText == officeId.ToString());
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

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

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

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

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

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

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

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

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

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Overall.Should().Be("red");
        result.Checks.Should().ContainSingle(c => c.Key == "estado_vehiculo" && c.Status == "fail");
    }

    // ── CF-03 (HU #10877): precondición registral — vehículo ya matriculado (RUNT/FLIT) ─────────

    [Fact]
    public async Task Post_Matricula_EstadoVehiculoActivoEnRunt_Bloquea422VehicleStateInvalid()
    {
        // AC1: el RUNT reporta el vehículo con estado_vehiculo "ok" (ACTIVO ⇒ ya matriculado/
        // circulando): bloqueo DURO 422 VEHICLE_STATE_INVALID_FOR_TYPE, snapshot NO se persiste.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(
            ("verifik", new StubProvider("verifik", Result("green", EstadoVehiculoCheck("ok")))));

        var (result, error, _, vehicleState) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().Be(VehicleStatePolicy.ErrorCode);
        result.Should().BeNull();
        vehicleState.Should().NotBeNull();
        vehicleState!.VehicleStatus.Should().Be(VehicleStatePolicy.VehicleStatusActivoRunt);
        vehicleState.ProcedureType.Should().Be(VehicleStatePolicy.ProcedureTypeMatriculaInicial);
        vehicleState.Source.Should().Be(VehicleStateSource.Runt);
        await _repo.DidNotReceive().AddPreflightSnapshotAsync(Arg.Any<ProcedureInstancePreflightSnapshot>(), ct);
    }

    [Fact]
    public async Task Post_Matricula_EstadoVehiculoDesconocido_Bloquea422VehicleStateInvalid()
    {
        // AC3: el RUNT no responde el estado del vehículo (check "unknown"): bloqueo DURO hasta poder
        // confirmar el estado — unknown YA NO preserva el semáforo en verde para esta familia.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(
            ("verifik", new StubProvider("verifik", Result("yellow", EstadoVehiculoCheck("unknown")))));

        var (result, error, _, vehicleState) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().Be(VehicleStatePolicy.ErrorCode);
        result.Should().BeNull();
        vehicleState.Should().NotBeNull();
        vehicleState!.VehicleStatus.Should().Be(VehicleStatePolicy.VehicleStatusDesconocido);
        vehicleState.Source.Should().Be(VehicleStateSource.RuntDesconocido);
    }

    [Fact]
    public async Task Post_Traspaso_EstadoVehiculoActivo_NoActivaCF03()
    {
        // CF-03 aplica SOLO a la familia Matrícula Inicial: en traspaso, "ok" (ACTIVO) es el estado
        // esperado (vehículo en circulación) y sigue sin bloquear.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: [Actor("comprador", "111"), Actor("vendedor", "222")]);
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(
            ("verifik", new StubProvider("verifik", Result("green", EstadoVehiculoCheck("ok")))),
            ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))),
            ("verifik_rnmc", new StubProvider("verifik_rnmc", Result("green", Check("ok")))));

        var (result, error, _, vehicleState) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        vehicleState.Should().BeNull();
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

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

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

        var (result, _, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

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

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

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

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Checks.Should().ContainSingle(c => c.Status == "error");
        result.Overall.Should().Be("red");
    }

    // ── HU #10538 (R3) / CF-03 (HU #10877, AC2): VIN ya matriculado ─────────────────────────────

    private RunPreflightHandler VehiculoOkHandler() =>
        HandlerWith(("verifik", new StubProvider("verifik", Result("green", Check("ok")))));

    [Fact]
    public async Task Post_Matricula_VinConAprobadaEnFlit_Bloquea422VehicleStateInvalid()
    {
        // CF-03 (HU #10877, AC2) — un VIN con una matrícula APROBADA en FLIT bloquea con
        // VEHICLE_STATE_INVALID_FOR_TYPE (422). ENDURECIDO: HU #10538/#10876 dejaban esto como check
        // informativo (warn) ofreciendo traspaso; CF-03 lo eleva a bloqueo DURO no subsanable — un VIN
        // aprobado no puede volver a matricularse (fuente FLIT, doble fuente con RUNT).
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

        var (result, error, _, vehicleState) =
            await VehiculoOkHandler().HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().Be(VehicleStatePolicy.ErrorCode);
        result.Should().BeNull();
        vehicleState.Should().NotBeNull();
        vehicleState!.VehicleStatus.Should().Be(VehicleStatePolicy.VehicleStatusAprobadoFlit);
        vehicleState.ProcedureType.Should().Be(VehicleStatePolicy.ProcedureTypeMatriculaInicial);
        vehicleState.Source.Should().Be(VehicleStateSource.Flit);
        // Bloqueo DURO: el snapshot no se persiste (mismo patrón que CF-01/DUPLICATE_ACTIVE_PROCEDURE).
        await _repo.DidNotReceive().AddPreflightSnapshotAsync(Arg.Any<ProcedureInstancePreflightSnapshot>(), ct);
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

        var (result, error, _, _) = await VehiculoOkHandler().HandleAsync(instance.Id, instance.TenantId, ct);

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

        var (result, error, _, _) = await VehiculoOkHandler().HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Checks.Should().NotContain(c => c.Key == "vin_matricula");
        result.Overall.Should().Be("green");
        instance.FieldValues.Should().NotContain(f => f.FieldKey == "vin_conflicto_traspaso");
    }

    // ── CF-01 (HU #10876): bloqueo DURO de duplicidad EN PROCESO por familia ───────────────────

    [Fact]
    public async Task Post_Matricula_DuplicadoActivoPorVin_Bloquea409()
    {
        // AC1: un trámite EN PROCESO (borrador) con el mismo VIN bloquea con
        // DUPLICATE_ACTIVE_PROCEDURE, sin persistir el snapshot.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        var existingId = Guid.NewGuid();
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        _repo.FindTramitesByVinAsync(instance.TenantId, Arg.Any<string>(), instance.Id, ct)
            .Returns(new List<VinTramiteExistente>
            {
                new(existingId, TramiteEstado.Borrador, Paso: 2, Placa: null, Vin: "1HGCM82633A004352"),
            });

        var (result, error, existingProcedureInstanceId, _) =
            await VehiculoOkHandler().HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().Be("DUPLICATE_ACTIVE_PROCEDURE");
        result.Should().BeNull();
        existingProcedureInstanceId.Should().Be(existingId);
        await _repo.DidNotReceive().AddPreflightSnapshotAsync(Arg.Any<ProcedureInstancePreflightSnapshot>(), ct);
    }

    [Fact]
    public async Task Post_Traspaso_DuplicadoActivoPorPlaca_Bloquea409()
    {
        // AC2: un trámite EN PROCESO (preparado) con la misma placa bloquea con
        // DUPLICATE_ACTIVE_PROCEDURE.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: [Actor("comprador", "111"), Actor("vendedor", "222")]);
        var existingId = Guid.NewGuid();
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        _repo.FindTramitesByPlacaAsync(instance.TenantId, Arg.Any<string>(), instance.Id, ct)
            .Returns(new List<PlacaTramiteExistente>
            {
                new(existingId, TramiteEstado.Preparado, Placa: "ABC123"),
            });
        var handler = HandlerWith(
            ("verifik", new StubProvider("verifik", Result("green", Check("ok")))),
            ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))),
            ("verifik_rnmc", new StubProvider("verifik_rnmc", Result("green", Check("ok")))));

        var (result, error, existingProcedureInstanceId, _) =
            await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().Be("DUPLICATE_ACTIVE_PROCEDURE");
        result.Should().BeNull();
        existingProcedureInstanceId.Should().Be(existingId);
    }

    [Fact]
    public async Task Post_OtraFamilia_NoActivaBloqueoDeDuplicidad()
    {
        // AC3: la activación es HARDCODED por familia — solo Matrícula Inicial y Traspaso bloquean.
        // Otro tipo de trámite ni siquiera consulta placa (FindTramitesByPlacaAsync no se invoca).
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("otro_tipo", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var (result, error, existingProcedureInstanceId, _) =
            await VehiculoOkHandler().HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        existingProcedureInstanceId.Should().BeNull();
        await _repo.DidNotReceive().FindTramitesByPlacaAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), ct);
    }

    [Fact]
    public async Task Post_Matricula_DuplicadoEnEstadoFinalAprobado_NoBloqueaPorCF01PeroBloqueaPorCF03()
    {
        // AC4 (CF-01/HU #10876): el estado final "aprobado" NO cuenta como "en proceso" — libera la
        // llave del VIN para la duplicidad EN PROCESO (nunca dispara 409 DUPLICATE_ACTIVE_PROCEDURE).
        // ENDURECIDO (CF-03/HU #10877, AC2): esa MISMA aprobada SÍ es fuente FLIT del bloqueo registral
        // — bloquea con 422 VEHICLE_STATE_INVALID_FOR_TYPE por una regla independiente (ver
        // Post_Matricula_VinConAprobadaEnFlit_Bloquea422VehicleStateInvalid).
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        _repo.FindTramitesByVinAsync(instance.TenantId, Arg.Any<string>(), instance.Id, ct)
            .Returns(new List<VinTramiteExistente>
            {
                new(Guid.NewGuid(), TramiteEstado.Aprobado, Paso: 5, Placa: null, Vin: "1HGCM82633A004352"),
            });

        var (result, error, existingProcedureInstanceId, _) =
            await VehiculoOkHandler().HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().Be(VehicleStatePolicy.ErrorCode); // CF-03, no CF-01.
        error.Should().NotBe("DUPLICATE_ACTIVE_PROCEDURE");
        result.Should().BeNull();
        existingProcedureInstanceId.Should().BeNull(); // CF-01 nunca se activó (no es "en proceso").
    }

    [Fact]
    public async Task Post_Traspaso_DuplicadoEnEstadoFinalAnulado_NoBloquea()
    {
        // AC4: "anulado" (final) libera la llave de placa.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: [Actor("comprador", "111"), Actor("vendedor", "222")]);
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        _repo.FindTramitesByPlacaAsync(instance.TenantId, Arg.Any<string>(), instance.Id, ct)
            .Returns(new List<PlacaTramiteExistente>
            {
                new(Guid.NewGuid(), TramiteEstado.Anulado, Placa: "ABC123"),
            });
        var handler = HandlerWith(
            ("verifik", new StubProvider("verifik", Result("green", Check("ok")))),
            ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))),
            ("verifik_rnmc", new StubProvider("verifik_rnmc", Result("green", Check("ok")))));

        var (result, error, existingProcedureInstanceId, _) =
            await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        existingProcedureInstanceId.Should().BeNull();
    }

    // ── A4/B4 (HU #10673) — transformaciones color/combustible: snapshots *_runt + no pisar ────

    private RunPreflightHandler VehiculoHydratesHandler(params HydratedField[] hydrated) =>
        BuildHandler(null,
        [
            ("kyverum_runt", new StubProvider("kyverum_runt",
                new ConsultationResult("kyverum_runt", "green", [Check("ok")], hydrated))),
        ]);

    private static string? ValueOf(ProcedureInstance instance, string key) =>
        instance.FieldValues.FirstOrDefault(f => f.FieldKey == key)?.ValueText;

    [Fact]
    public async Task Preflight_PrimeraConsulta_EscribeEfectivoYSnapshotRunt()
    {
        // Primera hidratación: no hay transformación → el efectivo y el snapshot RUNT quedan iguales al RUNT.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = VehiculoHydratesHandler(
            new HydratedField("vehicle_color", "PLATA", null),
            new HydratedField("vehicle_fuel", "GASOLINA", null));

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        instance.FieldValues.Should().ContainSingle(f =>
            f.FieldKey == "vehicle_color" && f.ValueText == "PLATA" && f.Source == "consultation");
        instance.FieldValues.Should().ContainSingle(f =>
            f.FieldKey == "vehicle_color_runt" && f.ValueText == "PLATA" && f.Source == "consultation");
        instance.FieldValues.Should().ContainSingle(f =>
            f.FieldKey == "vehicle_fuel" && f.ValueText == "GASOLINA");
        instance.FieldValues.Should().ContainSingle(f =>
            f.FieldKey == "vehicle_fuel_runt" && f.ValueText == "GASOLINA");
    }

    [Fact]
    public async Task Preflight_SinTransformacion_ReconsultaRefrescaEfectivoYSnapshot()
    {
        // Re-consulta sin transformación (efectivo == snapshot, sin flag): el RUNT nuevo pisa AMBOS.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vehicle_color", ValueText = "PLATA", Source = "consultation" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vehicle_color_runt", ValueText = "PLATA", Source = "consultation" });
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = VehiculoHydratesHandler(new HydratedField("vehicle_color", "AZUL", null));

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        ValueOf(instance, "vehicle_color").Should().Be("AZUL");
        ValueOf(instance, "vehicle_color_runt").Should().Be("AZUL");
    }

    [Fact]
    public async Task Preflight_FlagCambioActivo_NoPisaEfectivo_RefrescaSnapshot()
    {
        // Transformación DECLARADA (cambio_color = true): el efectivo se preserva; el snapshot RUNT sí se refresca.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vehicle_color", ValueText = "NEGRO", Source = "user" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vehicle_color_runt", ValueText = "PLATA", Source = "consultation" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "cambio_color", ValueText = "true", Source = "user" });
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = VehiculoHydratesHandler(new HydratedField("vehicle_color", "PLATA", null));

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        ValueOf(instance, "vehicle_color").Should().Be("NEGRO");       // cambio declarado intacto
        ValueOf(instance, "vehicle_color_runt").Should().Be("PLATA");  // snapshot RUNT refrescado
    }

    [Fact]
    public async Task Preflight_CambioImplicito_SinFlag_NoPisaEfectivo()
    {
        // Sin flag pero el efectivo YA difiere del snapshot previo → se trata como transformación activa.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vehicle_fuel", ValueText = "ELECTRICO", Source = "user" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vehicle_fuel_runt", ValueText = "GASOLINA", Source = "consultation" });
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = VehiculoHydratesHandler(new HydratedField("vehicle_fuel", "GASOLINA", null));

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        ValueOf(instance, "vehicle_fuel").Should().Be("ELECTRICO");
        ValueOf(instance, "vehicle_fuel_runt").Should().Be("GASOLINA");
    }

    [Fact]
    public async Task Preflight_ColorYCombustibleSimultaneos_PreservaAmbosCambios()
    {
        // Color y combustible declarados a la vez: la re-consulta conserva ambos efectivos y refresca ambos snapshots.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: Actor("comprador"), status: TramiteEstado.Borrador);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vehicle_color", ValueText = "NEGRO", Source = "user" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vehicle_color_runt", ValueText = "PLATA", Source = "consultation" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "cambio_color", ValueText = "true", Source = "user" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vehicle_fuel", ValueText = "ELECTRICO", Source = "user" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vehicle_fuel_runt", ValueText = "GASOLINA", Source = "consultation" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "cambio_combustible", ValueText = "true", Source = "user" });
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = BuildHandler(null,
        [
            ("kyverum_runt", new StubProvider("kyverum_runt", new ConsultationResult("kyverum_runt", "green", [Check("ok")],
            [
                new HydratedField("vehicle_color", "PLATA", null),
                new HydratedField("vehicle_fuel", "GASOLINA", null),
            ]))),
            ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))),
        ]);

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        ValueOf(instance, "vehicle_color").Should().Be("NEGRO");
        ValueOf(instance, "vehicle_fuel").Should().Be("ELECTRICO");
        ValueOf(instance, "vehicle_color_runt").Should().Be("PLATA");
        ValueOf(instance, "vehicle_fuel_runt").Should().Be("GASOLINA");
    }

    [Fact]
    public async Task Preflight_PrimeraConsultaCarroceria_EscribeEfectivoYSnapshotRunt()
    {
        // HU #10673 (A4/B4) — primera hidratación de carrocería: efectivo y snapshot quedan iguales al RUNT.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = VehiculoHydratesHandler(new HydratedField("vehicle_body_type", "SEDAN", null));

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        ValueOf(instance, "vehicle_body_type").Should().Be("SEDAN");
        ValueOf(instance, "vehicle_body_type_runt").Should().Be("SEDAN");
    }

    [Fact]
    public async Task Preflight_FlagCambioCarroceriaActivo_NoPisaEfectivo_RefrescaSnapshot()
    {
        // HU #10673 (A4/B4) — cambio_carroceria = true: el efectivo se preserva; el snapshot RUNT se refresca.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vehicle_body_type", ValueText = "PICKUP", Source = "user" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vehicle_body_type_runt", ValueText = "SEDAN", Source = "consultation" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "cambio_carroceria", ValueText = "true", Source = "user" });
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = VehiculoHydratesHandler(new HydratedField("vehicle_body_type", "SEDAN", null));

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        ValueOf(instance, "vehicle_body_type").Should().Be("PICKUP");       // cambio declarado intacto
        ValueOf(instance, "vehicle_body_type_runt").Should().Be("SEDAN");   // snapshot RUNT refrescado
    }

    [Fact]
    public async Task Preflight_CambioImplicitoCarroceria_SinFlag_NoPisaEfectivo()
    {
        // HU #10673 (A4/B4) — sin flag pero el efectivo ya difiere del snapshot previo → transformación activa.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vehicle_body_type", ValueText = "PICKUP", Source = "user" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vehicle_body_type_runt", ValueText = "SEDAN", Source = "consultation" });
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = VehiculoHydratesHandler(new HydratedField("vehicle_body_type", "SEDAN", null));

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        ValueOf(instance, "vehicle_body_type").Should().Be("PICKUP");
        ValueOf(instance, "vehicle_body_type_runt").Should().Be("SEDAN");
    }

    [Fact]
    public async Task Preflight_SinTransformacionCarroceria_ReconsultaRefrescaAmbos()
    {
        // HU #10673 (A4/B4) — re-consulta sin transformación de carrocería: el RUNT nuevo pisa efectivo y snapshot.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vehicle_body_type", ValueText = "SEDAN", Source = "consultation" });
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vehicle_body_type_runt", ValueText = "SEDAN", Source = "consultation" });
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = VehiculoHydratesHandler(new HydratedField("vehicle_body_type", "MICROBUS", null));

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        ValueOf(instance, "vehicle_body_type").Should().Be("MICROBUS");
        ValueOf(instance, "vehicle_body_type_runt").Should().Be("MICROBUS");
    }

    // ── FEATURE 05 (HU #10758) — fuente de comparendos → proveedor ───────────

    /// <summary>Override del tenant con solo la fuente de comparendos (sin cadenas ni timeout).</summary>
    private static ConsultationTenantOverride FuenteDeComparendos(string source) =>
        new(null, null, false, source);

    /// <summary>Los tres proveedores de comparendos registrados, cada uno devolviendo green.</summary>
    private static (string, IConsultationProvider)[] TodosLosProveedoresDeComparendos() =>
    [
        ("verifik", new StubProvider("verifik", Result("green", Check("ok")))),
        ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))),
        ("flit_fines", new StubProvider("flit_fines", Result("green", Check("ok")))),
        ("kyverum_fines", new StubProvider("kyverum_fines", Result("green", Check("ok")))),
    ];

    [Fact]
    public async Task Post_Traspaso_FuenteInterna_UsaFlitFinesParaAmbosActores()
    {
        // AC1: con fuente interna, ambos actores se consultan contra el API de FLIT.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: [Actor("comprador", "111"), Actor("vendedor", "222")]);
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(FuenteDeComparendos("internal"), TodosLosProveedoresDeComparendos());

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Provider.Should().Contain("flit_fines");
        result.Provider.Should().NotContain("verifik_simit");
        result.Provider.Should().NotContain("kyverum_fines");
    }

    [Fact]
    public async Task Post_Traspaso_FuenteInterna_ActorJuridico_TambienUsaFlitFines()
    {
        // AC1: la fuente manda — el tipo de persona NO desvía a Kyverum cuando la fuente es interna.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: [ActorNit("comprador", "900123456"), Actor("vendedor", "222")]);
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(FuenteDeComparendos("internal"), TodosLosProveedoresDeComparendos());

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Provider.Should().Contain("flit_fines");
        result.Provider.Should().NotContain("kyverum_fines");
    }

    [Fact]
    public async Task Post_Traspaso_FuenteExterna_CompradorNatural_UsaVerifikSimit()
    {
        // AC2: fuente externa + persona natural → proveedor SIMIT actual.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: [Actor("comprador", "111"), Actor("vendedor", "222")]);
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(FuenteDeComparendos("external"), TodosLosProveedoresDeComparendos());

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Provider.Should().Contain("verifik_simit");
        result.Provider.Should().NotContain("flit_fines");
    }

    [Fact]
    public async Task Post_Traspaso_FuenteExterna_CompradorConNit_UsaKyverumFines()
    {
        // AC2: fuente externa + persona jurídica → KYVERUM.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: [ActorNit("comprador", "900123456"), ActorNit("vendedor", "900999888")]);
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(FuenteDeComparendos("external"), TodosLosProveedoresDeComparendos());

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Provider.Should().Contain("kyverum_fines");
        result.Provider.Should().NotContain("verifik_simit");
    }

    [Fact]
    public async Task Post_Traspaso_FuenteExterna_CompradorNitVendedorCc_UsaUnProveedorDistintoPorActor()
    {
        // AC3 — el caso que rompería un resolver "por trámite" en vez de "por actor": en el MISMO
        // traspaso, el comprador jurídico va a Kyverum y el vendedor natural a Verifik.
        // Se verifica con providers que capturan su contexto: cada uno debe recibir EL DOCUMENTO DEL
        // ACTOR QUE LE CORRESPONDE, que es la prueba real del enrutado (el Source de los checks lo
        // pone el provider real, no el orquestador, así que no sirve para asertarlo aquí).
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: [ActorNit("comprador", "900123456"), Actor("vendedor", "222")]);
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var kyverum = new CapturingProvider("kyverum_fines", Result("green", Check("ok")));
        var verifikSimit = new CapturingProvider("verifik_simit", Result("green", Check("ok")));
        var handler = HandlerWith(FuenteDeComparendos("external"),
            ("verifik", new StubProvider("verifik", Result("green", Check("ok")))),
            ("kyverum_fines", kyverum),
            ("verifik_simit", verifikSimit));

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        // El comprador (NIT) fue a Kyverum...
        kyverum.LastContext!.FieldValues["owner_document_type"].Should().Be("NIT");
        kyverum.LastContext.FieldValues["owner_document_number"].Should().Be("900123456");
        // ...y el vendedor (CC) a Verifik, en el mismo trámite.
        verifikSimit.LastContext!.FieldValues["owner_document_type"].Should().Be("CC");
        verifikSimit.LastContext.FieldValues["owner_document_number"].Should().Be("222");
        // Ambos proveedores quedan registrados en la traza del snapshot.
        result!.Provider.Should().Contain("kyverum_fines").And.Contain("verifik_simit");
    }

    [Fact]
    public async Task Post_Traspaso_SinOverrideDeTenant_CaeAExterna()
    {
        // AC4: compañía sin fila de configuración operativa (GetAsync ⇒ null) ⇒ fuente externa,
        // el default del DDL. Es un camino distinto al de "fila con el valor por defecto".
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: [Actor("comprador", "111"), Actor("vendedor", "222")]);
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(TodosLosProveedoresDeComparendos()); // NullOverrideProvider

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Provider.Should().Contain("verifik_simit");
        result.Provider.Should().NotContain("flit_fines");
    }

    [Fact]
    public async Task Post_Traspaso_ProveedorDeComparendosNoRegistrado_AgregaCheckError()
    {
        // Si el resolver apunta a un proveedor ausente del registro, degrada a check "error" con la
        // key del actor, sin lanzar. Es la ventana que evita registrar los proveedores antes de
        // cablear el resolver (HU10756/10757 van antes que esta).
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: [Actor("comprador", "111"), Actor("vendedor", "222")]);
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        // flit_fines NO registrado, pero la compañía pide fuente interna.
        var handler = HandlerWith(FuenteDeComparendos("internal"),
            ("verifik", new StubProvider("verifik", Result("green", Check("ok")))));

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Checks.Should().Contain(c => c.Key == "simit_comprador" && c.Status == "error");
        result.Checks.Single(c => c.Key == "simit_comprador").Source.Should().Be("flit_fines");
    }

    [Fact]
    public async Task Post_Traspaso_ActorSinDocumento_AgregaUnknownConElProveedorResuelto()
    {
        // Sin actor no hay a quién consultar: unknown (no bloquea), etiquetado con el proveedor que
        // la fuente resolvería. Las guardas van en este orden porque elegir proveedor exige el actor.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: Actor("comprador", "111")); // sin vendedor
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = HandlerWith(FuenteDeComparendos("internal"), TodosLosProveedoresDeComparendos());

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        var vendedor = result!.Checks.Single(c => c.Key == "simit_vendedor");
        vendedor.Status.Should().Be("unknown");
        vendedor.Source.Should().Be("flit_fines");
    }

    [Fact]
    public async Task Post_Traspaso_ComparendosWarn_OverallEsYellowNoRed()
    {
        // AC5 del Feature, end-to-end en el orquestador: los comparendos advierten y el pre-vuelo
        // queda amarillo, así que el wizard no añade el blocker preflight_red.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: [Actor("comprador", "111"), Actor("vendedor", "222")]);
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var multasWarn = Result("yellow",
            new ConsultationCheck(FinesCheckFactory.KeyMultas, FinesCheckFactory.LabelMultas, "warn", "flit_fines",
                "2 multa(s) pendiente(s) por $500.000 COP"));
        var handler = HandlerWith(FuenteDeComparendos("internal"),
            ("verifik", new StubProvider("verifik", Result("green", Check("ok")))),
            ("flit_fines", new StubProvider("flit_fines", multasWarn)));

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Overall.Should().Be("yellow");
        result.Checks.Should().Contain(c => c.Key == $"simit_comprador_{FinesCheckFactory.KeyMultas}" && c.Status == "warn");
    }

    // ── FEATURE 05 — severidad de bloqueo configurable por criterio y OT ──────────

    [Fact]
    public async Task Post_Matricula_SoatFail_ConBlocksFalse_SeDegradaAWarn_YOverallEsYellow()
    {
        // La compañía marcó SOAT como informativo para el OT destino: el fail del RUNT se baja a warn
        // y el pre-vuelo queda amarillo (no rojo), así que el wizard no bloquea con preflight_red.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var vehiculo = new StubProvider("kyverum_runt", new ConsultationResult("kyverum_runt", "red",
            [new ConsultationCheck("soat", "SOAT", "fail", "kyverum_runt", "SOAT vencido o no vigente")], []));
        var handler = BuildHandler(null, [("kyverum_runt", vehiculo)],
            blockingPolicy: new StubBlockingPolicy(("soat", false)));

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Checks.Single(c => c.Key == "soat").Status.Should().Be("warn");
        result.Overall.Should().Be("yellow");
    }

    [Fact]
    public async Task Post_Matricula_SoatFail_SinConfig_SigueBloqueandoEnRojo()
    {
        // Sin configuración (default del criterio soat=bloquea) el comportamiento previo se preserva.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("matricula_inicial", actors: Actor("comprador"));
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var vehiculo = new StubProvider("kyverum_runt", new ConsultationResult("kyverum_runt", "red",
            [new ConsultationCheck("soat", "SOAT", "fail", "kyverum_runt", "SOAT vencido o no vigente")], []));
        var handler = BuildHandler(null, [("kyverum_runt", vehiculo)]);

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Checks.Single(c => c.Key == "soat").Status.Should().Be("fail");
        result.Overall.Should().Be("red");
    }

    [Fact]
    public async Task Post_Traspaso_ComparendosWarn_ConBlocksTrue_SeElevaAFail_YOverallEsRed()
    {
        // La compañía marcó comparendos como bloqueantes para el OT destino: el warn se eleva a fail
        // y el pre-vuelo queda rojo (el OT no admite trámites con multas pendientes).
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: [Actor("comprador", "111"), Actor("vendedor", "222")]);
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var multasWarn = Result("yellow",
            new ConsultationCheck(FinesCheckFactory.KeyMultas, FinesCheckFactory.LabelMultas, "warn", "flit_fines",
                "2 multa(s) pendiente(s) por $500.000 COP"));
        var handler = BuildHandler(FuenteDeComparendos("internal"),
            [
                ("verifik", new StubProvider("verifik", Result("green", Check("ok")))),
                ("flit_fines", new StubProvider("flit_fines", multasWarn)),
            ],
            blockingPolicy: new StubBlockingPolicy(("fines", true)));

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Checks.Should().Contain(c =>
            c.Key == $"simit_comprador_{FinesCheckFactory.KeyMultas}" && c.Status == "fail");
        result.Overall.Should().Be("red");
    }

    // ── FEATURE 05 (HU #10760) — el preflight omite lo que la compañía inhabilitó para el OT ─────

    /// <summary>Traspaso con OT destino ya elegido: es el par (tenant, OT) sobre el que hay política.</summary>
    private static ProcedureInstance TraspasoConOtDestino()
    {
        var instance = Instance("traspaso", actors: [Actor("comprador", "111"), Actor("vendedor", "222")]);
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            FieldKey = "transit_office_id",
            ValueText = Guid.NewGuid().ToString(),
            Source = "user",
        });
        return instance;
    }

    [Fact]
    public async Task Post_Traspaso_ComparendosRestringidos_NoConsultaNingunActor_YLoIndica()
    {
        // AC2: la restricción es del par (tenant, OT), no del rol → ni comprador ni vendedor se
        // consultan, y se deja UN solo check de omisión (no uno por actor).
        var ct = TestContext.Current.CancellationToken;
        var instance = TraspasoConOtDestino();
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var simit = new CapturingProvider("verifik_simit", Result("green", Check("ok")));
        var handler = BuildHandler(
            null,
            [
                ("verifik", new StubProvider("verifik", Result("green", Check("ok")))),
                ("verifik_simit", simit),
            ],
            restrictionPolicy: new CountingRestrictionPolicy(ConsultationRestrictionKinds.Fines));

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        simit.LastContext.Should().BeNull(); // ningún actor se consultó.
        result!.Provider.Should().NotContain("verifik_simit");
        result.Checks.Should().NotContain(c => c.Key.StartsWith("simit_comprador", StringComparison.Ordinal));
        result.Checks.Should().NotContain(c => c.Key.StartsWith("simit_vendedor", StringComparison.Ordinal));

        var omitida = result.Checks.Should().ContainSingle(c => c.Key == "simit_omitida").Subject;
        omitida.Status.Should().Be("unknown");
        omitida.Source.Should().Be("system");
        omitida.Label.Should().Be("Consulta de comparendos omitida");
        omitida.Message.Should().Be(
            "No se consultaron los comparendos: la compañía tiene esta consulta inhabilitada para el organismo de tránsito de destino.");
        result.Overall.Should().Be("green");
    }

    [Fact]
    public async Task Post_Restricciones_SeLeenUnaSolaVezPorCorrida()
    {
        // AC3: una sola lectura por corrida, aunque el fan-out consulte varios actores y tipos.
        var ct = TestContext.Current.CancellationToken;
        var instance = TraspasoConOtDestino();
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var policy = new CountingRestrictionPolicy(); // sin restricciones: corre todo el fan-out.
        var handler = BuildHandler(
            null,
            [
                ("verifik", new StubProvider("verifik", Result("green", Check("ok")))),
                ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))),
            ],
            restrictionPolicy: policy);

        var (_, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        policy.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Post_SinOtResoluble_SinRestricciones_LasConsultasCorrenNormalmente()
    {
        // Sin OT destino en field_values la política no tiene par al que aplicar → default permisivo.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance("traspaso", actors: [Actor("comprador", "111"), Actor("vendedor", "222")]);
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);
        var handler = BuildHandler(
            null,
            [
                ("verifik", new StubProvider("verifik", Result("green", Check("ok")))),
                ("verifik_simit", new StubProvider("verifik_simit", Result("green", Check("ok")))),
            ],
            restrictionPolicy: new CountingRestrictionPolicy()); // sin kinds inhabilitados.

        var (result, error, _, _) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Provider.Should().Contain("verifik_simit");
        result.Checks.Should().NotContain(c => c.Key == "simit_omitida");
    }

    [Fact]
    public void ComposeOverall_SoloUnknownDeOmision_EsGreen()
    {
        // La razón de elegir unknown y no warn: una compañía con restricciones activas NO vive en
        // amarillo permanente (el amarillo sigue significando "hallazgo").
        RunPreflightHandler.ComposeOverall(
        [
            new PreflightCheckDto("rnmc_omitida", "Consulta RNMC omitida", "unknown", "system", null),
            new PreflightCheckDto("simit_omitida", "Consulta de comparendos omitida", "unknown", "system", null),
        ]).Should().Be("green");
    }

    [Fact]
    public void ComposeOverall_OmisionMasWarn_SigueSiendoYellow()
    {
        // La omisión no ENMASCARA un hallazgo real de otra consulta.
        RunPreflightHandler.ComposeOverall(
        [
            new PreflightCheckDto("rnmc_omitida", "Consulta RNMC omitida", "unknown", "system", null),
            new PreflightCheckDto("b", "B", "warn", "s", null),
        ]).Should().Be("yellow");
    }

    [Fact]
    public void ComposeOverall_OmisionMasFail_SigueSiendoRed()
    {
        RunPreflightHandler.ComposeOverall(
        [
            new PreflightCheckDto("simit_omitida", "Consulta de comparendos omitida", "unknown", "system", null),
            new PreflightCheckDto("b", "B", "fail", "s", null),
        ]).Should().Be("red");
    }
}
