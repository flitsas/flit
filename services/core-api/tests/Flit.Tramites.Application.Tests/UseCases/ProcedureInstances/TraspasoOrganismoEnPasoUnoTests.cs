using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #11200 — en TRASPASO el organismo no se elige: lo impone el RUNT, según dónde esté matriculado
/// el vehículo. Lo que esta HU adelanta al primer paso es la comprobación de que en ese organismo se
/// puede radicar; antes solo se sabía al final, al intentar entregar el trámite.
///
/// <para>La comprobación del paso 1 <b>no sustituye</b> a la de la radicación
/// (<c>TramiteLifecycleService</c>): un trámite puede quedar en borrador y perder la habilitación
/// antes de radicarse. Son dos comprobaciones sobre el mismo hecho en dos momentos distintos — ver
/// <c>TramiteLifecycleServiceTests</c> para la segunda (AC4).</para>
/// </summary>
public sealed class TraspasoOrganismoEnPasoUnoTests
{
    private const string OtDelRunt = "SECRETARÍA DE MOVILIDAD DE MEDELLÍN";
    private static readonly Guid OtId = Guid.Parse("55555555-5555-5555-5555-555555555200");

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly InMemoryPreflightPreviewStore _store = new();
    private readonly IOtOperabilityGate _operabilidad = Substitute.For<IOtOperabilityGate>();

    /// <summary>Resolver por nombre: devuelve el OT solo si la compañía lo tiene habilitado.</summary>
    private sealed class ResolverPorNombre(bool habilitado) : ITransitOfficeResolver
    {
        public Task<ResolvedTransitOffice?> ResolveEnabledByNameAsync(
            Guid tenantId, string transitOfficeName, CancellationToken ct = default) =>
            Task.FromResult(habilitado
                ? new ResolvedTransitOffice(OtId, "05001000", transitOfficeName, "05001")
                : null);

        public Task<ResolvedTransitOffice?> ResolveEnabledByIdAsync(
            Guid tenantId, Guid transitOfficeId, CancellationToken ct = default) =>
            Task.FromResult<ResolvedTransitOffice?>(null);
    }

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

    /// <summary>Consulta por placa que devuelve el organismo de matrícula, como hace el RUNT.</summary>
    private static ConsultationResult VehiculoConOt(string? nombreOt) =>
        new("stub", "green",
            [new ConsultationCheck("estado_vehiculo", "Estado del vehículo", "ok", "stub", "ACTIVO")],
            nombreOt is null ? [] : [new HydratedField("transit_office_name", nombreOt, null)]);

    private (RunPreflightPreviewHandler Handler, StubProvider Vehiculo, StubProvider Simit) Handler(
        bool habilitado,
        string? nombreOt = OtDelRunt)
    {
        var vehiculo = new StubProvider("kyverum_runt", VehiculoConOt(nombreOt));
        var simit = new StubProvider("verifik_simit", new ConsultationResult("verifik_simit", "green", [], []));
        var registry = new StaticRegistry(new Dictionary<string, IConsultationProvider>
        {
            ["kyverum_runt"] = vehiculo,
            ["verifik_simit"] = simit,
        });

        var handler = new RunPreflightPreviewHandler(
            _repo,
            registry,
            new ConsultationProviderChainResolver(registry, new ConsultationChainOptions()),
            new NullOverrideProvider(),
            NullConsultationRestrictionPolicy.Instance,
            _store,
            new ResolverPorNombre(habilitado),
            NullConsultationBlockingPolicy.Instance,
            validationPolicy: null,
            otOperability: _operabilidad);

        return (handler, vehiculo, simit);
    }

    private static PreflightPreviewRequest Traspaso(Guid tenant) =>
        new(tenant, TramiteModalidadEntradaCodes.Traspaso, null, "ABC123", "CC", "1020304050", null);

    private Guid TenantSinDuplicados()
    {
        var tenant = Guid.NewGuid();
        _repo.FindTramitesByPlacaAsync(tenant, Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
        return tenant;
    }

    // ── AC1 — organismo activo y habilitado ───────────────────────────────────

    [Fact]
    public async Task AC1_OrganismoActivoYHabilitado_ElTramiteContinua()
    {
        var tenant = TenantSinDuplicados();
        _operabilidad.IsOperableAsync(OtId, Arg.Any<CancellationToken>()).Returns(true);
        var (handler, _, _) = Handler(habilitado: true);

        var (result, error, _, _) = await handler.HandleAsync(Traspaso(tenant), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.PreviewToken.Should().NotBeNullOrWhiteSpace();
        // El paso 1 llegó hasta el final: la segunda pasada de duplicidad (la que cierra la ventana de
        // carrera, justo antes de guardar la consulta) solo corre si nada bloqueó antes.
        await _repo.Received(2).FindTramitesByPlacaAsync(
            tenant, Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── AC2 — organismo no operativo en la plataforma ─────────────────────────

    [Fact]
    public async Task AC2_OrganismoNoOperativoEnFlit_NoPermiteContinuar()
    {
        var tenant = TenantSinDuplicados();
        // Grant vigente, pero el organismo está desactivado a nivel plataforma: la radicación lo
        // rechazaría igual, así que no tiene sentido dejar avanzar el trámite.
        _operabilidad.IsOperableAsync(OtId, Arg.Any<CancellationToken>()).Returns(false);
        var (handler, _, _) = Handler(habilitado: true);

        var (result, error, _, _) = await handler.HandleAsync(Traspaso(tenant), TestContext.Current.CancellationToken);

        error.Should().Be(TransitOfficeSelectionPolicy.UnavailableErrorCode);
        result.Should().BeNull();
        // Se corta ahí mismo: no se llega a la segunda pasada del paso 1 ni se guarda consulta alguna,
        // así que no queda nada con lo que crear el trámite.
        await _repo.Received(1).FindTramitesByPlacaAsync(
            tenant, Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── AC3 — organismo activo pero no habilitado para la compañía ────────────

    [Fact]
    public async Task AC3_OrganismoSinGrantParaLaCompania_NoPermiteContinuar()
    {
        var tenant = TenantSinDuplicados();
        _operabilidad.IsOperableAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var (handler, vehiculo, _) = Handler(habilitado: false);

        var (result, error, _, _) = await handler.HandleAsync(Traspaso(tenant), TestContext.Current.CancellationToken);

        error.Should().Be(TransitOfficeSelectionPolicy.UnavailableErrorCode);
        result.Should().BeNull();
        // El vehículo sí se consultó —de ahí sale el organismo—, pero el paso 1 no llega a su final.
        vehiculo.Calls.Should().Be(1);
        await _repo.Received(1).FindTramitesByPlacaAsync(
            tenant, Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── Degradación segura ────────────────────────────────────────────────────

    [Fact]
    public async Task SinNombreDeOrganismoEnElRunt_NoSeBloqueaElTramite()
    {
        var tenant = TenantSinDuplicados();
        _operabilidad.IsOperableAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var (handler, _, _) = Handler(habilitado: false, nombreOt: null);

        var (result, error, _, _) = await handler.HandleAsync(Traspaso(tenant), TestContext.Current.CancellationToken);

        // Sin dato no hay nada que validar: negarle el trámite al gestor por un fallo del RUNT sería
        // castigarlo por algo ajeno. La comprobación de la radicación sigue cubriendo el caso.
        error.Should().BeNull();
        result.Should().NotBeNull();
        await _operabilidad.DidNotReceiveWithAnyArgs()
            .IsOperableAsync(default, TestContext.Current.CancellationToken);
    }

    // ── AC5 — trámites en curso ───────────────────────────────────────────────

    /// <summary>
    /// AC5 — un borrador creado antes del cambio conserva el comportamiento anterior. Se cumple por
    /// construcción: la comprobación vive en la consulta del paso 1 SIN trámite creado (el preview),
    /// que por definición solo corre en trámites nuevos. El preflight de una instancia ya existente no
    /// la ejecuta — sigue resolviendo el organismo del RUNT en silencio y, si no lo halla, dejando solo
    /// el nombre (B11, HU #10659). Sin migración ni bloqueo del trabajo en curso.
    /// </summary>
    [Fact]
    public async Task AC5_ElPreflightDeUnaInstanciaExistente_NoAplicaElBloqueoDelPasoUno()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        var instancia = new Flit.Tramites.Domain.Entities.ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoTraspasoStandard ?? TramiteModalidadEntradaCodes.Traspaso),
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRA-2026-000001",
            Status = Flit.Tramites.Domain.Tramites.Estados.TramiteEstado.Borrador,
            ModalidadEntrada = TramiteModalidadEntradaCodes.Traspaso,
            TipologiaCodigo = TramiteTipologiaCatalog.CodigoTraspasoStandard,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instancia.FieldValues.Add(new Flit.Tramites.Domain.Entities.ProcedureInstanceFieldValue
        {
            FieldKey = "plate",
            ValueText = "ABC123",
            Source = "user",
        });

        _repo.GetByIdWithWizardGraphAsync(instancia.Id, tenant, ct).Returns(instancia);
        _repo.FindTramitesByPlacaAsync(tenant, Arg.Any<string>(), Arg.Any<Guid>(), ct).Returns([]);

        var vehiculo = new StubProvider("kyverum_runt", VehiculoConOt(OtDelRunt));
        var registry = new StaticRegistry(new Dictionary<string, IConsultationProvider>
        {
            ["kyverum_runt"] = vehiculo,
            ["verifik_simit"] = new StubProvider("verifik_simit", new ConsultationResult("verifik_simit", "green", [], [])),
        });
        var preflight = new RunPreflightHandler(
            _repo,
            registry,
            new ConsultationProviderChainResolver(registry, new ConsultationChainOptions()),
            new NullOverrideProvider(),
            NullConsultationRestrictionPolicy.Instance,
            new ResolverPorNombre(habilitado: false), // el OT del RUNT NO está habilitado
            NullConsultationBlockingPolicy.Instance);

        var (result, error, _, _) = await preflight.HandleAsync(instancia.Id, tenant, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        // Comportamiento de siempre: se conserva el nombre del RUNT y no se inventa un id.
        instancia.FieldValues.Should().Contain(f => f.FieldKey == "transit_office_name");
        instancia.FieldValues.Should().NotContain(f => f.FieldKey == "transit_office_id");
    }
}
