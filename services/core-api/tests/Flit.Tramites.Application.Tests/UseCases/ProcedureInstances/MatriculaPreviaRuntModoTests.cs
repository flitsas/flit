using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.ReadModels;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Epic #12550 (HU #12648) — la regla «ya matriculado por historial» en el preflight de la INSTANCIA
/// (<see cref="RunPreflightHandler"/>) y bajo los tres modos de la HU #10970: block bloquea, warn deja
/// el hallazgo amarillo y sigue, off no toca nada. Complementa a
/// <see cref="MatriculaRutaRuntPreviewTests"/>, que cubre el paso 1 sin trámite.
/// </summary>
public sealed class MatriculaPreviaRuntModoTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

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

    private static ConsultationCheck Estado(string status, string estado) =>
        new("estado_vehiculo", "Estado del vehículo", status, "stub", estado);

    private static ConsultationCheck Historial(string veredicto) =>
        new(KyverumRuntVehicleResultMapper.CheckMatriculaPreviaRunt, "Historial de matrícula en el RUNT", "ok", "stub", null,
            Datos: ConsultationCheckDetail.Datos((KyverumRuntVehicleResultMapper.DatoVeredicto, veredicto)));

    private static ProcedureInstance Matricula()
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "vin", ValueText = "9FBHJD209VM679113", Source = "user" });
        return instance;
    }

    private RunPreflightHandler Handler(TramiteValidationMode registral, params ConsultationCheck[] vehiculo)
    {
        var providers = new Dictionary<string, IConsultationProvider>
        {
            ["verifik"] = new StubProvider("verifik", new ConsultationResult("stub", "green", vehiculo, [])),
            ["verifik_simit"] = new StubProvider("verifik_simit", new ConsultationResult("stub", "green", [], [])),
            ["verifik_rnmc"] = new StubProvider("verifik_rnmc", new ConsultationResult("stub", "green", [], [])),
        };
        var registry = new StaticRegistry(providers);
        return new RunPreflightHandler(
            _repo, registry,
            new ConsultationProviderChainResolver(registry, new ConsultationChainOptions()),
            new NullOverrideProvider(), NullConsultationRestrictionPolicy.Instance, NullTransitOfficeResolver.Instance,
            NullConsultationBlockingPolicy.Instance,
            new TramiteValidationPolicy(TramiteValidationMode.Block, registral));
    }

    private ProcedureInstance Preparar()
    {
        var instance = Matricula();
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(instance);
        _repo.FindTramitesByVinAsync(instance.TenantId, Arg.Any<string>(), instance.Id, Arg.Any<CancellationToken>())
            .Returns(new List<VinTramiteExistente>());
        return instance;
    }

    [Fact]
    public async Task Block_MatriculaEnHistorial_ConEstadoRegistrado_Bloquea()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Preparar();

        var (result, error, _, vehicleState) = await Handler(
                TramiteValidationMode.Block, Estado("fail", "REGISTRADO"), Historial(KyverumRuntVehicleResultMapper.VeredictoMatriculado))
            .HandleAsync(instance.Id, instance.TenantId, ct);

        result.Should().BeNull();
        error.Should().Be(VehicleStatePolicy.ErrorCode);
        vehicleState!.VehicleStatus.Should().Be(VehicleStatePolicy.VehicleStatusActivoRunt);
        await _repo.DidNotReceive().AddPreflightSnapshotAsync(Arg.Any<ProcedureInstancePreflightSnapshot>(), ct);
    }

    [Fact]
    public async Task Block_SinMatriculaEnHistorial_ConEstadoActivo_NoBloquea()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Preparar();

        var (result, error, _, vehicleState) = await Handler(
                TramiteValidationMode.Block, Estado("ok", "ACTIVO"), Historial(KyverumRuntVehicleResultMapper.VeredictoSinMatricula))
            .HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        vehicleState.Should().BeNull();
        result!.Checks.Single(c => c.Key == "estado_vehiculo").Status.Should().Be("ok");
        result.Checks.Single(c => c.Key == KyverumRuntVehicleResultMapper.CheckMatriculaPreviaRunt).Status.Should().Be("ok");
    }

    [Fact]
    public async Task Warn_MatriculaEnHistorial_NoBloquea_YDejaElHallazgoAmarillo()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Preparar();

        var (result, error, _, vehicleState) = await Handler(
                TramiteValidationMode.Warn, Estado("ok", "ACTIVO"), Historial(KyverumRuntVehicleResultMapper.VeredictoMatriculado))
            .HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        vehicleState.Should().BeNull();
        var previa = result!.Checks.Single(c => c.Key == KyverumRuntVehicleResultMapper.CheckMatriculaPreviaRunt);
        previa.Status.Should().Be("warn");
        previa.Message.Should().Contain("ya se encuentra matriculado");
        result.Overall.Should().Be("yellow");
    }

    [Fact]
    public async Task Off_MatriculaEnHistorial_NoTocaNada()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Preparar();

        var (result, error, _, _) = await Handler(
                TramiteValidationMode.Off, Estado("ok", "ACTIVO"), Historial(KyverumRuntVehicleResultMapper.VeredictoMatriculado))
            .HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.Checks.Single(c => c.Key == KyverumRuntVehicleResultMapper.CheckMatriculaPreviaRunt).Status.Should().Be("ok");
        result.Checks.Single(c => c.Key == "estado_vehiculo").Status.Should().Be("ok");
    }
}
