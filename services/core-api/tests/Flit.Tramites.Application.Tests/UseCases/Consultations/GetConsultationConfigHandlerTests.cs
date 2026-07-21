using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU #10478 — el handler que expone al operador el proveedor primario de consulta por tipo. Sin
/// override devuelve los defaults Kyverum-first; con override del tenant refleja la elección (p. ej.
/// placa en Verifik), que el wizard usa para decidir si pide el tipo de documento del propietario.
/// </summary>
public sealed class GetConsultationConfigHandlerTests
{
    private static CancellationToken Ct() => TestContext.Current.CancellationToken;

    private static GetConsultationConfigHandler Handler(ConsultationTenantOverride? tenantOverride) =>
        new(new ConsultationProviderChainResolver(new EmptyRegistry(), new ConsultationChainOptions()),
            new StubOverrideProvider(tenantOverride));

    [Fact]
    public async Task SinOverride_DevuelveDefaultsKyverumFirst()
    {
        var result = await Handler(tenantOverride: null).HandleAsync(Guid.NewGuid(), Ct());

        result.VehicleVin.Should().Be("kyverum_runt");
        result.VehiclePlate.Should().Be("kyverum_runt");
        result.Conductor.Should().Be("kyverum_runt_conductor");
    }

    [Fact]
    public async Task OverridePlacaVerifik_SeReflejaEnPrimarioDePlaca()
    {
        var tenantOverride = new ConsultationTenantOverride(
            new Dictionary<string, ConsultationChainSelection>(StringComparer.OrdinalIgnoreCase)
            {
                ["vehicle_plate"] = new("verifik", ["kyverum_runt"]),
            },
            FailoverTimeoutMs: null);

        var result = await Handler(tenantOverride).HandleAsync(Guid.NewGuid(), Ct());

        result.VehiclePlate.Should().Be("verifik");     // el tenant lo cambió
        result.VehicleVin.Should().Be("kyverum_runt");  // los otros siguen el default
        result.Conductor.Should().Be("kyverum_runt_conductor");
    }

    // FEATURE 02 — el flag only_own_vehicles del tenant se refleja para que el wizard adapte la captura.
    [Fact]
    public async Task SinOverride_OnlyOwnVehiclesEsFalse()
    {
        var result = await Handler(tenantOverride: null).HandleAsync(Guid.NewGuid(), Ct());
        result.OnlyOwnVehicles.Should().BeFalse();
    }

    [Fact]
    public async Task ConOnlyOwnVehicles_SeReflejaEnElResultado()
    {
        var tenantOverride = new ConsultationTenantOverride(
            Chains: null, FailoverTimeoutMs: null, OnlyOwnVehicles: true);

        var result = await Handler(tenantOverride).HandleAsync(Guid.NewGuid(), Ct());

        result.OnlyOwnVehicles.Should().BeTrue();
    }

    private sealed class StubOverrideProvider(ConsultationTenantOverride? value)
        : IConsultationTenantOverrideProvider
    {
        public Task<ConsultationTenantOverride?> GetAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult(value);
    }

    // ResolveChain no invoca providers, así que un registry vacío basta.
    private sealed class EmptyRegistry : IConsultationProviderRegistry
    {
        public IConsultationProvider? Resolve(string key) => null;
    }
}
