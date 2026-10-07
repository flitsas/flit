using Flit.Api.Grpc;
using Flit.Ict.Grpc.Contracts;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace Flit.Infrastructure.Tests.Ict;

/// <summary>
/// Bug #13304 (D6 + campos 9-12) — la consulta de vehículo que hace ICT por gRPC. En traspaso (placa +
/// documento) NO viaja el VIN, igual que el paso 1 del wizard; y la respuesta trae el resultado completo
/// (<c>vehicle_snapshot_json</c>) para que el borrador lo reutilice sin re-consultar.
/// <para>Uso de ejemplo: <c>new IctConsultationService(chain, registry, overrides).Query(request, ctx)</c>.</para>
/// </summary>
public sealed class IctConsultationServiceVehicleTests
{
    private static readonly Guid Tenant = Guid.Parse("b1330400-0000-4000-8000-000000000001");

    private static ConsultationResult ResultadoRunt() => new(
        "kyverum_runt",
        "yellow",
        [
            new ConsultationCheck("gravamenes", "Gravámenes", "warn", "kyverum_runt", "Prenda vigente"),
            new ConsultationCheck("soat", "SOAT", "ok", "kyverum_runt", null),
        ],
        [
            new HydratedField("runt_tiene_gravamenes", "SI", null),
            new HydratedField("vehicle_year", "2020", null),
        ]);

    [Fact]
    public async Task Vehicle_TraspasoConPlacaDocumentoYVin_ConsultaPorPlacaSinVinYDevuelveElSnapshot()
    {
        var chain = new CapturingChain(ResultadoRunt());
        var service = new IctConsultationService(chain, new EmptyRegistry(), new NullOverride());
        var antes = DateTimeOffset.UtcNow;

        var reply = await service.Query(new ConsultationRequest
        {
            TenantId = Tenant.ToString(),
            QueryType = "VEHICLE",
            Plate = "ABC123",
            Vin = "1HGCM82633A004352",
            DocumentType = "CC",
            DocumentNumber = "1013304001",
        }, new TestCallContext(TestContext.Current.CancellationToken));

        chain.Calls.Should().Be(1);
        chain.LastKind.Should().Be(ConsultationKind.VehiclePlate);
        chain.LastContext!.FieldValues.Should().NotContainKey("vin", "paridad con el paso 1 del wizard de traspaso");
        chain.LastContext.FieldValues["plate"].Should().Be("ABC123");
        chain.LastContext.FieldValues["owner_document_number"].Should().Be("1013304001");

        reply.ErrorCode.Should().BeEmpty();
        reply.ConsultationKind.Should().Be("VehiclePlate");
        reply.Provider.Should().Be("kyverum_runt");
        reply.ConsultedAt.Should().NotBeNull();
        reply.ConsultedAt.ToDateTimeOffset().Should().BeOnOrAfter(antes.AddSeconds(-1));
        PreflightVehicleSnapshotJson.TryDeserialize(reply.VehicleSnapshotJson, out var snapshot).Should().BeTrue();
        snapshot!.HydratedFields.Should().Contain(new HydratedField("runt_tiene_gravamenes", "SI", null));
        snapshot.Checks.Should().Contain(c => c.Key == "gravamenes" && c.Status == "warn");
        reply.SoatStatus.Should().Be("VIGENTE", "los hechos reducidos siguen saliendo igual");
        reply.VehicleModelYear.Should().Be(2020);
    }

    [Fact]
    public async Task Vehicle_PlacaSinDocumento_ConservaElVin()
    {
        var chain = new CapturingChain(ResultadoRunt());
        var service = new IctConsultationService(chain, new EmptyRegistry(), new NullOverride());

        await service.Query(new ConsultationRequest
        {
            TenantId = Tenant.ToString(),
            QueryType = "VEHICLE",
            Plate = "ABC123",
            Vin = "1HGCM82633A004352",
        }, new TestCallContext(TestContext.Current.CancellationToken));

        chain.LastContext!.FieldValues["vin"].Should().Be("1HGCM82633A004352");
    }

    [Fact]
    public async Task Vin_ConsultaPorVin_ConservaElVinYMarcaLaKind()
    {
        var chain = new CapturingChain(ResultadoRunt());
        var service = new IctConsultationService(chain, new EmptyRegistry(), new NullOverride());

        var reply = await service.Query(new ConsultationRequest
        {
            TenantId = Tenant.ToString(),
            QueryType = "VIN",
            Vin = "1HGCM82633A004352",
            DocumentType = "CC",
            DocumentNumber = "1013304001",
        }, new TestCallContext(TestContext.Current.CancellationToken));

        chain.LastContext!.FieldValues["vin"].Should().Be("1HGCM82633A004352");
        reply.ConsultationKind.Should().Be("VehicleVin");
        reply.VehicleSnapshotJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Rnmc_NoLlenaElSnapshotDeVehiculo()
    {
        var chain = new CapturingChain(ResultadoRunt());
        var service = new IctConsultationService(chain, new EmptyRegistry(), new NullOverride());

        var reply = await service.Query(new ConsultationRequest
        {
            TenantId = Tenant.ToString(),
            QueryType = "RNMC",
            DocumentType = "CC",
            DocumentNumber = "1013304001",
        }, new TestCallContext(TestContext.Current.CancellationToken));

        chain.Calls.Should().Be(0);
        reply.VehicleSnapshotJson.Should().BeEmpty();
        reply.ConsultedAt.Should().BeNull();
    }

    private sealed class CapturingChain(ConsultationResult result) : IConsultationProviderChainResolver
    {
        public int Calls { get; private set; }
        public ConsultationKind? LastKind { get; private set; }
        public ConsultationContext? LastContext { get; private set; }

        public IReadOnlyList<string> ResolveChain(ConsultationKind kind, ConsultationTenantOverride? tenantOverride = null) =>
            ["kyverum_runt"];

        public Task<ConsultationResult> ConsultAsync(
            ConsultationKind kind, ConsultationContext ctx, ConsultationTenantOverride? tenantOverride, CancellationToken ct)
        {
            Calls++;
            LastKind = kind;
            LastContext = ctx;
            return Task.FromResult(result);
        }
    }

    private sealed class EmptyRegistry : IConsultationProviderRegistry
    {
        public IConsultationProvider? Resolve(string providerKey) => null;
    }

    private sealed class NullOverride : IConsultationTenantOverrideProvider
    {
        public Task<ConsultationTenantOverride?> GetAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult<ConsultationTenantOverride?>(null);
    }

    /// <summary><see cref="ServerCallContext"/> mínimo: el servicio solo lee el token de cancelación.</summary>
    private sealed class TestCallContext(CancellationToken ct) : ServerCallContext
    {
        protected override string MethodCore => "/flit.ict.v1.IctConsultation/Query";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:127.0.0.1:0";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = [];
        protected override CancellationToken CancellationTokenCore => ct;
        protected override Metadata ResponseTrailersCore { get; } = [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore { get; } = new(null, []);

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
            throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
