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
/// Epic #12550 (HU #12648, ADR-0059 §Ruta Corta) — el paso 1 de una matrícula inicial dice por dónde
/// va el trámite a partir de lo que devuelve el RUNT, y «ya matriculado» se lee del historial de
/// solicitudes antes que del estado del automotor. Los escenarios reproducen las siete capturas
/// reales del 2026-09-17 (preasignado: REGISTRADO + placa + solicitud de preasignación con entidad;
/// matriculado: ACTIVO + «MATRÍCULA INICIAL» AUTORIZADA) y las combinaciones que no aparecieron pero
/// la regla debe soportar.
/// </summary>
public sealed class MatriculaRutaRuntPreviewTests
{
    private const string Vin = "9FBHJD209VM679113";
    private const string OtEnvigado = "STRIA TTEyTTO ENVIGADO";
    private static readonly Guid EnvigadoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SecretariaElegida = Guid.Parse("11111111-1111-1111-1111-111111111199");

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ITransitOfficeResolver _resolver = Substitute.For<ITransitOfficeResolver>();
    private readonly IOtOperabilityGate _operability = Substitute.For<IOtOperabilityGate>();
    private readonly InMemoryPreflightPreviewStore _store = new();

    private sealed class StubProvider(ConsultationResult result) : IConsultationProvider
    {
        public string Key => "kyverum_runt";
        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct) =>
            Task.FromResult(result with { Provider = Key });
    }

    private sealed class StaticRegistry(IConsultationProvider provider) : IConsultationProviderRegistry
    {
        public IConsultationProvider? Resolve(string providerKey) => providerKey == provider.Key ? provider : null;
    }

    private sealed class NullOverrideProvider : IConsultationTenantOverrideProvider
    {
        public Task<ConsultationTenantOverride?> GetAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult<ConsultationTenantOverride?>(null);
    }

    public MatriculaRutaRuntPreviewTests()
    {
        _repo.FindTramitesByVinAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _resolver.ResolveEnabledByIdAsync(Arg.Any<Guid>(), SecretariaElegida, Arg.Any<CancellationToken>())
            .Returns(new ResolvedTransitOffice(SecretariaElegida, "05001000", "Secretaría de Medellín", "05001"));
        _operability.IsOperableAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private RunPreflightPreviewHandler Handler(ConsultationResult runt)
    {
        var registry = new StaticRegistry(new StubProvider(runt));
        return new RunPreflightPreviewHandler(
            _repo, registry,
            new ConsultationProviderChainResolver(registry, new ConsultationChainOptions()),
            new NullOverrideProvider(), NullConsultationRestrictionPolicy.Instance, _store, _resolver,
            NullConsultationBlockingPolicy.Instance, otOperability: _operability);
    }

    private void EnvigadoHabilitado() =>
        _resolver.ResolveEnabledByNameAsync(Arg.Any<Guid>(), OtEnvigado, Arg.Any<CancellationToken>())
            .Returns(new ResolvedTransitOffice(EnvigadoId, "05266000", "Secretaría de Envigado", "05266", "ENVIGADO"));

    private static ConsultationCheck Estado(string status, string estado) =>
        new("estado_vehiculo", "Estado del vehículo", status, "kyverum_runt", estado,
            Datos: ConsultationCheckDetail.Datos(("Estado", estado)));

    private static ConsultationCheck Historial(string veredicto, string organismo) =>
        new(KyverumRuntVehicleResultMapper.CheckMatriculaPreviaRunt, "Historial de matrícula en el RUNT", "ok", "kyverum_runt", null,
            Datos: ConsultationCheckDetail.Datos(
                (KyverumRuntVehicleResultMapper.DatoVeredicto, veredicto), ("Organismo", organismo)));

    /// <summary>Lo que el mapper produce para un vehículo con placa preasignada (WVT948, Envigado).</summary>
    private static ConsultationResult Preasignado(string estado = "REGISTRADO", string estadoStatus = "fail") =>
        new("kyverum_runt", "yellow",
            [Estado(estadoStatus, estado), Historial(KyverumRuntVehicleResultMapper.VeredictoSinMatricula, OtEnvigado)],
            [new HydratedField("plate", "WVT948", null), new HydratedField("transit_office_name", OtEnvigado, null)]);

    /// <summary>Lo que el mapper produce para un vehículo ya matriculado (WVN492, Medellín).</summary>
    private static ConsultationResult Matriculado(string estado = "ACTIVO", string estadoStatus = "ok") =>
        new("kyverum_runt", "green",
            [Estado(estadoStatus, estado), Historial(KyverumRuntVehicleResultMapper.VeredictoMatriculado, "STRIA DE TTOyTTE MEDELLIN")],
            [new HydratedField("plate", "WVN492", null), new HydratedField("transit_office_name", "STRIA DE TTOyTTE MEDELLIN", null)]);

    private static ConsultationResult SinPlaca() =>
        new("kyverum_runt", "yellow", [Estado("fail", "REGISTRADO")], [new HydratedField("vehicle_brand", "RENAULT", null)]);

    private static PreflightPreviewRequest Matricula(Guid? secretaria = null) =>
        new(Guid.NewGuid(), TramiteModalidadEntradaCodes.MatriculaInicial, Vin, null, null, null, secretaria);

    private static PreflightCheckDto Check(PreflightPreviewDto dto, string key) => dto.Checks.Single(c => c.Key == key);

    // ── AC1 / AC6: placa preasignada ⇒ Ruta Corta, sin bloqueo, organismo del RUNT ─────────────

    [Fact]
    public async Task PlacaPreasignada_RutaCorta_ConElOrganismoDeLaSolicitudResuelto()
    {
        EnvigadoHabilitado();

        var (dto, error, _, _) = await Handler(Preasignado()).HandleAsync(Matricula(), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        dto!.Route.Should().Be(MatriculaRuta.Corta);
        dto.TransitOffice.Should().BeEquivalentTo(new PreflightPreviewTransitOfficeDto(EnvigadoId, "05266000", "Secretaría de Envigado", "ENVIGADO"));
        var ruta = Check(dto, RunPreflightHandler.CheckRutaMatricula);
        ruta.Status.Should().Be("ok");
        ruta.Message.Should().Contain("WVT948").And.Contain("Secretaría de Envigado");
        // Sin bloqueo registral: el estado REGISTRADO queda informativo, como siempre.
        Check(dto, "estado_vehiculo").Status.Should().Be("warn");
    }

    [Fact]
    public async Task PlacaPreasignada_ConEstadoActivo_NoSeBloquea_PorqueElHistorialNoRegistraMatricula()
    {
        EnvigadoHabilitado();
        // Convenio concesionario–OT: la placa ya está montada en el RUNT y el automotor sale ACTIVO,
        // pero el historial solo tiene la preasignación. Con la regla anterior esto era «ya matriculado».
        var (dto, error, _, vehicleState) = await Handler(Preasignado("ACTIVO", "ok"))
            .HandleAsync(Matricula(), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        vehicleState.Should().BeNull();
        dto!.Route.Should().Be(MatriculaRuta.Corta);
        Check(dto, "estado_vehiculo").Status.Should().Be("ok");
    }

    // ── AC5: matrícula inicial en el historial ⇒ bloqueo (traspaso), aunque el estado no sea ACTIVO ──

    [Theory]
    [InlineData("ACTIVO", "ok")]
    [InlineData("REGISTRADO", "fail")]
    public async Task MatriculaInicialEnElHistorial_Bloquea_ConElMismoCodigoDeSiempre(string estado, string status)
    {
        var (dto, error, _, vehicleState) = await Handler(Matriculado(estado, status))
            .HandleAsync(Matricula(), TestContext.Current.CancellationToken);

        dto.Should().BeNull();
        error.Should().Be(VehicleStatePolicy.ErrorCode);
        vehicleState!.VehicleStatus.Should().Be(VehicleStatePolicy.VehicleStatusActivoRunt,
            "el frontend ofrece el traspaso con este código y no hace falta uno nuevo");
        vehicleState.Source.Should().Be(VehicleStateSource.Runt);
    }

    // ── AC7: sin historial ⇒ la regla de siempre por estado ──────────────────────────────────

    [Fact]
    public async Task SinHistorial_ActivoSigueBloqueando()
    {
        var activoSinHistorial = new ConsultationResult("kyverum_runt", "green",
            [Estado("ok", "ACTIVO")], [new HydratedField("plate", "QYQ132", null)]);

        var (_, error, _, vehicleState) = await Handler(activoSinHistorial)
            .HandleAsync(Matricula(), TestContext.Current.CancellationToken);

        error.Should().Be(VehicleStatePolicy.ErrorCode);
        vehicleState!.VehicleStatus.Should().Be(VehicleStatePolicy.VehicleStatusActivoRunt);
    }

    [Fact]
    public async Task SinHistorial_RegistradoConPlaca_EsRutaCorta()
    {
        EnvigadoHabilitado();
        var registrado = new ConsultationResult("kyverum_runt", "yellow",
            [Estado("fail", "REGISTRADO")],
            [new HydratedField("plate", "WVT948", null), new HydratedField("transit_office_name", OtEnvigado, null)]);

        var (dto, error, _, _) = await Handler(registrado).HandleAsync(Matricula(), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        dto!.Route.Should().Be(MatriculaRuta.Corta);
        dto.TransitOffice!.Id.Should().Be(EnvigadoId);
    }

    // ── AC2: sin placa ⇒ Ruta Larga, el gestor elige ─────────────────────────────────────────

    [Fact]
    public async Task SinPlaca_RutaLarga_SinOrganismo()
    {
        var (dto, error, _, _) = await Handler(SinPlaca()).HandleAsync(Matricula(SecretariaElegida), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        dto!.Route.Should().Be(MatriculaRuta.Larga);
        dto.TransitOffice.Should().BeNull();
        Check(dto, RunPreflightHandler.CheckRutaMatricula).Message.Should().Contain("no tiene placa").And.NotContain("Ruta");
        await _resolver.DidNotReceive().ResolveEnabledByNameAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── AC3: organismo del RUNT no habilitado para la compañía ⇒ bloqueo con el nombre ───────

    [Fact]
    public async Task OrganismoDelRuntNoHabilitado_Bloquea_YDiceCual()
    {
        _resolver.ResolveEnabledByNameAsync(Arg.Any<Guid>(), OtEnvigado, Arg.Any<CancellationToken>())
            .Returns((ResolvedTransitOffice?)null);

        var (dto, error, _, vehicleState) = await Handler(Preasignado()).HandleAsync(Matricula(), TestContext.Current.CancellationToken);

        dto.Should().BeNull();
        error.Should().Be(VehicleStatePolicy.OrganismoRuntNoHabilitadoErrorCode);
        vehicleState!.VehicleStatus.Should().Be(VehicleStatePolicy.VehicleStatusOrganismoRuntNoHabilitado);
        vehicleState.Detalle.Should().Be(OtEnvigado);
    }

    [Fact]
    public async Task OrganismoDelRuntHabilitadoPeroNoOperable_Bloquea()
    {
        EnvigadoHabilitado();
        _operability.IsOperableAsync(EnvigadoId, Arg.Any<CancellationToken>()).Returns(false);

        var (_, error, _, vehicleState) = await Handler(Preasignado()).HandleAsync(Matricula(), TestContext.Current.CancellationToken);

        error.Should().Be(VehicleStatePolicy.OrganismoRuntNoHabilitadoErrorCode);
        vehicleState!.Detalle.Should().Be(OtEnvigado);
    }

    [Fact]
    public async Task PlacaSinOrganismoEnElRunt_RutaCortaSinOrganismo_NoBloquea()
    {
        var sinOrganismo = new ConsultationResult("kyverum_runt", "yellow",
            [Estado("fail", "REGISTRADO")], [new HydratedField("plate", "WVT948", null)]);

        var (dto, error, _, _) = await Handler(sinOrganismo).HandleAsync(Matricula(), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        dto!.Route.Should().Be(MatriculaRuta.Corta);
        dto.TransitOffice.Should().BeNull();
    }

    // ── AC8: fuera de matrícula inicial nada de esto aplica ──────────────────────────────────

    [Fact]
    public async Task Traspaso_NoDecideRuta_YElHistorialConMatriculaNoBloquea()
    {
        // Un traspaso consulta por placa un vehículo matriculado: el historial trae MATRÍCULA INICIAL
        // y eso es lo normal, no un bloqueo.
        _resolver.ResolveEnabledByNameAsync(Arg.Any<Guid>(), "STRIA DE TTOyTTE MEDELLIN", Arg.Any<CancellationToken>())
            .Returns(new ResolvedTransitOffice(Guid.NewGuid(), "05001000", "Secretaría de Medellín", "05001"));
        var request = new PreflightPreviewRequest(
            Guid.NewGuid(), TramiteModalidadEntradaCodes.Traspaso, null, "WVN492", "CC", "1000", null);

        var (dto, error, _, _) = await Handler(Matriculado()).HandleAsync(request, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        dto!.Route.Should().BeNull();
        dto.TransitOffice.Should().BeNull();
        dto.Checks.Should().NotContain(c => c.Key == RunPreflightHandler.CheckRutaMatricula);
        Check(dto, KyverumRuntVehicleResultMapper.CheckMatriculaPreviaRunt).Status.Should().Be("ok");
    }
}
