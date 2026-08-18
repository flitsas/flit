using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #11199 — la secretaría de tránsito se elige en el PRIMER paso de la matrícula inicial y es
/// requisito para consultar el VIN. Hasta ahora el organismo se elegía en el último paso: el gestor
/// recorría el trámite entero para descubrir al final que no podía radicar donde pensaba.
///
/// <para>HU #11200 — la contraparte del traspaso: allí el organismo lo impone el RUNT (donde está
/// matriculado el vehículo) y lo que se adelanta al paso 1 es la VALIDACIÓN de que ese organismo
/// sirve. Ver <c>TraspasoOrganismoEnPasoUnoTests</c>.</para>
/// </summary>
public sealed class TransitOfficeEnPasoUnoTests
{
    private const string VinLibre = "1HGCM82633A004352";
    private static readonly Guid Habilitada = Guid.Parse("33333333-3333-3333-3333-333333333199");
    private static readonly Guid NoHabilitada = Guid.Parse("44444444-4444-4444-4444-444444444199");

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly InMemoryPreflightPreviewStore _store = new();

    /// <summary>
    /// Resolver realista: solo <see cref="Habilitada"/> pasa el filtro de "activa en el catálogo y con
    /// grant vigente". Cualquier otro id devuelve null, que es exactamente lo que hace el resolver real
    /// tanto con un organismo desactivado como con uno sin grant — el motivo no se distingue.
    /// </summary>
    private sealed class SoloUnaHabilitada : ITransitOfficeResolver
    {
        public Task<ResolvedTransitOffice?> ResolveEnabledByNameAsync(
            Guid tenantId, string transitOfficeName, CancellationToken ct = default) =>
            Task.FromResult<ResolvedTransitOffice?>(null);

        public Task<ResolvedTransitOffice?> ResolveEnabledByIdAsync(
            Guid tenantId, Guid transitOfficeId, CancellationToken ct = default) =>
            Task.FromResult(transitOfficeId == Habilitada
                ? new ResolvedTransitOffice(Habilitada, "05001000", "Secretaría de Movilidad de Medellín", "05001")
                : null);
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

    private static ConsultationResult VehiculoOk() =>
        new("stub", "green",
            [new ConsultationCheck("estado_vehiculo", "Estado del vehículo", "fail", "stub", "REGISTRADO")],
            []);

    private (RunPreflightPreviewHandler Handler, StubProvider Provider) PreviewHandler()
    {
        var provider = new StubProvider("kyverum_runt", VehiculoOk());
        var registry = new StaticRegistry(new Dictionary<string, IConsultationProvider> { ["kyverum_runt"] = provider });
        var handler = new RunPreflightPreviewHandler(
            _repo,
            registry,
            new ConsultationProviderChainResolver(registry, new ConsultationChainOptions()),
            new NullOverrideProvider(),
            NullConsultationRestrictionPolicy.Instance,
            _store,
            new SoloUnaHabilitada(),
            NullConsultationBlockingPolicy.Instance);
        return (handler, provider);
    }

    private static PreflightPreviewRequest Matricula(Guid tenant, Guid? secretaria) =>
        new(tenant, TramiteModalidadEntradaCodes.MatriculaInicial, VinLibre, null, null, null, secretaria);

    private static PreflightPreviewRequest Traspaso(Guid tenant) =>
        new(tenant, TramiteModalidadEntradaCodes.Traspaso, null, "ABC123", "CC", "1020304050", null);

    private void SinDuplicados(Guid tenant)
    {
        _repo.FindTramitesByVinAsync(tenant, Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _repo.FindTramitesByPlacaAsync(tenant, Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    // ── AC1/AC2 — la secretaría habilita la consulta ──────────────────────────

    [Fact]
    public async Task AC1_ConSecretariaElegida_LaConsultaCorreNormalmente()
    {
        var tenant = Guid.NewGuid();
        SinDuplicados(tenant);
        var (handler, provider) = PreviewHandler();

        var (result, error, _, _) = await handler.HandleAsync(
            Matricula(tenant, Habilitada), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.PreviewToken.Should().NotBeNullOrWhiteSpace();
        provider.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SinSecretaria_LaConsultaCorre_ElOrganismoSeEligeDespues()
    {
        var tenant = Guid.NewGuid();
        SinDuplicados(tenant);
        var (handler, provider) = PreviewHandler();

        var (result, error, _, _) = await handler.HandleAsync(
            Matricula(tenant, null), TestContext.Current.CancellationToken);

        // El paso 1 pregunta dónde se radica DESPUÉS de identificar el vehículo: exigir el organismo
        // aquí obligaba a elegirlo a ciegas, antes de saber siquiera de qué vehículo se habla. El
        // requisito sigue vivo en la creación (CreateFromConsultaCommand), que es cuando el
        // organismo tiene que quedar guardado; aquí no se persiste nada.
        error.Should().BeNull();
        result.Should().NotBeNull();
        provider.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SecretariaEnGuidVacio_SeTrataComoAusente()
    {
        var tenant = Guid.NewGuid();
        SinDuplicados(tenant);
        var (handler, _) = PreviewHandler();

        // `Guid.Empty` es "no elegida", no un organismo inválido: ni se intenta resolver ni se
        // rechaza — se consulta igual que cuando no viene organismo.
        var (result, error, _, _) = await handler.HandleAsync(
            Matricula(tenant, Guid.Empty), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result.Should().NotBeNull();
    }

    // ── AC3 — solo organismos activos y habilitados ───────────────────────────

    [Fact]
    public async Task AC3_SecretariaNoActivaNiHabilitada_SeRechazaAunqueVengaEnLaPeticion()
    {
        var tenant = Guid.NewGuid();
        SinDuplicados(tenant);
        var (handler, provider) = PreviewHandler();

        // La lista del selector pudo cargarse antes de que el administrador desactivara el organismo o
        // revocara el grant: el servidor no puede confiar en lo que llega del navegador.
        var (result, error, _, _) = await handler.HandleAsync(
            Matricula(tenant, NoHabilitada), TestContext.Current.CancellationToken);

        error.Should().Be(TransitOfficeSelectionPolicy.UnavailableErrorCode);
        result.Should().BeNull();
        provider.Calls.Should().Be(0);
    }

    // ── AC5 — solo aplica a matrícula inicial ─────────────────────────────────

    [Fact]
    public async Task AC5_TraspasoSinSecretaria_SigueConsultandoComoSiempre()
    {
        var tenant = Guid.NewGuid();
        SinDuplicados(tenant);
        var (handler, provider) = PreviewHandler();

        var (result, error, _, _) = await handler.HandleAsync(
            Traspaso(tenant), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result.Should().NotBeNull();
        provider.Calls.Should().Be(1);
    }

    // ── AC4 — la elección queda escrita con el trámite ────────────────────────

    /// <summary>
    /// AC4 — el organismo se persiste en <c>field_values</c> al crear el trámite, junto con la marca
    /// <c>transit_office_origen=paso_1</c>. Esa marca es la que permite al paso del FUR mostrar el
    /// organismo en vez de pedirlo, sin afectar a los borradores anteriores al cambio (D8).
    /// </summary>
    [Fact]
    public async Task AC4_AlCrearElTramite_ElOrganismoQuedaEnFieldValuesConSuOrigen()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        SinDuplicados(tenant);

        PrepararCreacion(tenant);
        var handler = FromConsultaHandler();

        var (_, error, _, _) = await handler.HandleAsync(
            new CreateFromConsultaRequest(
                tenant,
                Guid.NewGuid(),
                TramiteModalidadEntradaCodes.MatriculaInicial,
                VinLibre,
                null,
                null,
                null,
                PreviewToken: null,
                TransitOfficeId: Habilitada),
            ct);

        error.Should().BeNull();
        var campos = _creada!.FieldValues;
        campos.Should().Contain(f => f.FieldKey == "transit_office_id" && f.ValueText == Habilitada.ToString());
        campos.Should().Contain(f => f.FieldKey == "transit_office_name"
            && f.ValueText == "Secretaría de Movilidad de Medellín");
        campos.Should().Contain(f => f.FieldKey == "transit_office_code" && f.ValueText == "05001000");
        campos.Should().Contain(f =>
            f.FieldKey == TransitOfficeSelectionPolicy.OrigenFieldKey
            && f.ValueText == TransitOfficeSelectionPolicy.OrigenPasoUno);
        // El id también viaja a la columna de la instancia, que es la que leen el motor de reglas OT y
        // la bandeja del organismo.
        _creada.TransitOfficeId.Should().Be(Habilitada);
    }

    [Fact]
    public async Task AC3_AlCrearElTramite_UnaSecretariaNoHabilitadaNoDejaRegistro()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = Guid.NewGuid();
        SinDuplicados(tenant);
        var handler = FromConsultaHandler();

        var (result, error, _, _) = await handler.HandleAsync(
            new CreateFromConsultaRequest(
                tenant,
                Guid.NewGuid(),
                TramiteModalidadEntradaCodes.MatriculaInicial,
                VinLibre,
                null,
                null,
                null,
                PreviewToken: null,
                TransitOfficeId: NoHabilitada),
            ct);

        error.Should().Be(TransitOfficeSelectionPolicy.UnavailableErrorCode);
        result.Should().BeNull();
        await _repo.DidNotReceiveWithAnyArgs().SaveChangesAsync(ct);
    }

    // ── Andamiaje de la creación ──────────────────────────────────────────────

    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();

    private CreateProcedureInstanceFromConsultaHandler FromConsultaHandler()
    {
        var registry = new StaticRegistry(new Dictionary<string, IConsultationProvider>
        {
            ["kyverum_runt"] = new StubProvider("kyverum_runt", VehiculoOk()),
        });
        var preflight = new RunPreflightHandler(
            _repo,
            registry,
            new ConsultationProviderChainResolver(registry, new ConsultationChainOptions()),
            new NullOverrideProvider(),
            NullConsultationRestrictionPolicy.Instance,
            NullTransitOfficeResolver.Instance,
            NullConsultationBlockingPolicy.Instance);

        return new CreateProcedureInstanceFromConsultaHandler(
            _repo,
            new CreateProcedureInstanceHandler(_repo, _typeRepo),
            new PatchFieldValuesHandler(_repo),
            preflight,
            Substitute.For<IPreflightPreviewStore>(),
            new SoloUnaHabilitada());
    }

    /// <summary>La instancia que el handler dio de alta, capturada al vuelo por el doble del repositorio.</summary>
    private Flit.Tramites.Domain.Entities.ProcedureInstance? _creada;

    /// <summary>
    /// Deja practicable el camino de la creación: tipo MATRICULA_NUEVA publicado y referencia generada
    /// como lo hace el repositorio real. La instancia que nace queda accesible en <see cref="_creada"/>
    /// —y devuelta por las lecturas posteriores del repositorio— para poder inspeccionar los
    /// <c>field_values</c> que el handler escribió sobre ella.
    /// </summary>
    private void PrepararCreacion(Guid tenant)
    {
        _typeRepo.GetByCodePublishedAsync("MATRICULA_NUEVA", Arg.Any<CancellationToken>())
            .Returns(new Flit.Tramites.Domain.Entities.ProcedureType
            {
                Id = Guid.NewGuid(),
                Code = "MATRICULA_NUEVA",
                Name = "Matrícula nueva",
                Family = "matriculas",
                PublicationStatus = Flit.Tramites.Domain.Enums.PublicationStatus.Published,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        _repo.AddWithUniqueReferenceAsync(
                Arg.Any<Flit.Tramites.Domain.Entities.ProcedureInstance>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var instancia = call.Arg<Flit.Tramites.Domain.Entities.ProcedureInstance>();
                instancia.ReferenceNumber = "MAT-2026-000001";
                _creada = instancia;
                _repo.GetByIdWithDetailsAsync(instancia.Id, tenant, Arg.Any<CancellationToken>()).Returns(instancia);
                _repo.GetByIdWithWizardGraphAsync(instancia.Id, tenant, Arg.Any<CancellationToken>()).Returns(instancia);
                return Task.FromResult(AddProcedureInstanceOutcome.Created);
            });
    }
}
