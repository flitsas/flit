using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU #10478 — cadena de proveedores Kyverum-first con fallback a Verifik. Verifica orden de cadena,
/// fallback solo ante checks <c>error</c> (no ante "no encontrado"), salto de providers no
/// registrados, presupuesto de failover por timeout, y que el primario sano no invoca al fallback.
/// </summary>
public sealed class ConsultationProviderChainResolverTests
{
    private static readonly ConsultationContext Ctx =
        new(Guid.Empty, Guid.Empty, "traspaso", new Dictionary<string, string?>());

    // ── ResolveChain ────────────────────────────────────────────────────────────────────────
    [Fact]
    public void ResolveChain_UsaOrdenConfigurado()
    {
        var resolver = Resolver(Chains(vin: ["kyverum_runt", "verifik"]));

        resolver.ResolveChain(ConsultationKind.VehicleVin)
            .Should().Equal("kyverum_runt", "verifik");
    }

    [Fact]
    public void ResolveChain_SinConfig_UsaDefaultsEmbebidosKyverumFirst()
    {
        var resolver = Resolver(new ConsultationChainOptions()); // DefaultChains vacío

        resolver.ResolveChain(ConsultationKind.VehiclePlate).Should().Equal("kyverum_runt", "verifik");
        resolver.ResolveChain(ConsultationKind.Conductor).Should().Equal("kyverum_runt_conductor", "verifik_conductor");
    }

    // ── Fallback ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task PrimarioError_CaeAlFallbackYDevuelveElSecundario()
    {
        var kyverum = new FakeProvider("kyverum_runt", Errored("kyverum_runt"));
        var verifik = new FakeProvider("verifik", Healthy("verifik"));
        var resolver = Resolver(Chains(vin: ["kyverum_runt", "verifik"]), kyverum, verifik);

        var result = await resolver.ConsultAsync(ConsultationKind.VehicleVin, Ctx, failoverTimeoutMs: null, Ct());

        result.Provider.Should().Be("verifik");
        result.Overall.Should().Be("green");
        kyverum.Calls.Should().Be(1);
        verifik.Calls.Should().Be(1);
    }

    [Fact]
    public async Task PrimarioSano_NoInvocaAlFallback()
    {
        var kyverum = new FakeProvider("kyverum_runt", Healthy("kyverum_runt"));
        var verifik = new FakeProvider("verifik", Healthy("verifik"));
        var resolver = Resolver(Chains(vin: ["kyverum_runt", "verifik"]), kyverum, verifik);

        var result = await resolver.ConsultAsync(ConsultationKind.VehicleVin, Ctx, failoverTimeoutMs: null, Ct());

        result.Provider.Should().Be("kyverum_runt");
        kyverum.Calls.Should().Be(1);
        verifik.Calls.Should().Be(0);
    }

    [Fact]
    public async Task NoEncontrado_EsDefinitivo_NoCaeAlFallback()
    {
        // "fail" (vehículo no existe en el RUNT) es una respuesta definitiva: no se reintenta con Verifik.
        var kyverum = new FakeProvider("kyverum_runt", NotFound("kyverum_runt"));
        var verifik = new FakeProvider("verifik", Healthy("verifik"));
        var resolver = Resolver(Chains(vin: ["kyverum_runt", "verifik"]), kyverum, verifik);

        var result = await resolver.ConsultAsync(ConsultationKind.VehicleVin, Ctx, failoverTimeoutMs: null, Ct());

        result.Provider.Should().Be("kyverum_runt");
        verifik.Calls.Should().Be(0);
    }

    [Fact]
    public async Task TodosError_DevuelveElUltimoResultado()
    {
        var kyverum = new FakeProvider("kyverum_runt", Errored("kyverum_runt"));
        var verifik = new FakeProvider("verifik", Errored("verifik"));
        var resolver = Resolver(Chains(vin: ["kyverum_runt", "verifik"]), kyverum, verifik);

        var result = await resolver.ConsultAsync(ConsultationKind.VehicleVin, Ctx, failoverTimeoutMs: null, Ct());

        result.Provider.Should().Be("verifik");
        result.Checks.Should().Contain(c => c.Status == "error");
        verifik.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ProviderPrimarioNoRegistrado_SaltaAlSiguiente()
    {
        // Solo verifik está registrado; kyverum_runt no. La cadena lo salta sin fallar.
        var verifik = new FakeProvider("verifik", Healthy("verifik"));
        var resolver = Resolver(Chains(vin: ["kyverum_runt", "verifik"]), verifik);

        var result = await resolver.ConsultAsync(ConsultationKind.VehicleVin, Ctx, failoverTimeoutMs: null, Ct());

        result.Provider.Should().Be("verifik");
        verifik.Calls.Should().Be(1);
    }

    [Fact]
    public async Task NingunProviderDeLaCadenaRegistrado_DevuelveError()
    {
        // Cadena con keys reales pero sin ningún provider registrado en el registry → error sintético.
        var resolver = Resolver(Chains(vin: ["kyverum_runt", "verifik"]));

        var result = await resolver.ConsultAsync(ConsultationKind.VehicleVin, Ctx, failoverTimeoutMs: null, Ct());

        result.Provider.Should().Be("chain");
        result.Checks.Should().Contain(c => c.Status == "error");
    }

    // ── Timeout de failover ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task PrimarioLento_ExcedeFailoverTimeout_CaeAlFallback()
    {
        var kyverum = new FakeProvider("kyverum_runt", Healthy("kyverum_runt"), delay: TimeSpan.FromSeconds(30));
        var verifik = new FakeProvider("verifik", Healthy("verifik"));
        var resolver = Resolver(Chains(vin: ["kyverum_runt", "verifik"]), kyverum, verifik);

        var result = await resolver.ConsultAsync(ConsultationKind.VehicleVin, Ctx, failoverTimeoutMs: 50, Ct());

        result.Provider.Should().Be("verifik");
        verifik.Calls.Should().Be(1);
    }

    // ── Fixtures de prueba ──────────────────────────────────────────────────────────────────
    private static CancellationToken Ct() => TestContext.Current.CancellationToken;

    private static ConsultationChainOptions Chains(List<string>? vin = null) =>
        new() { DefaultChains = { [ConsultationKindKeys.VehicleVin] = vin ?? [] } };

    private static ConsultationProviderChainResolver Resolver(
        ConsultationChainOptions options, params IConsultationProvider[] providers) =>
        new(new FakeRegistry(providers), options);

    private static ConsultationResult Healthy(string provider) =>
        new(provider, "green", [new ConsultationCheck("estado_vehiculo", "", "ok", provider, null)], []);

    private static ConsultationResult Errored(string provider) =>
        new(provider, "red", [new ConsultationCheck("provider", "", "error", provider, "no verificable")], []);

    private static ConsultationResult NotFound(string provider) =>
        new(provider, "red", [new ConsultationCheck("vehiculo", "", "fail", provider, "no encontrado")], []);

    private sealed class FakeProvider(string key, ConsultationResult result, TimeSpan? delay = null)
        : IConsultationProvider
    {
        public int Calls { get; private set; }

        public string Key => key;

        public async Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            Calls++;
            if (delay is { } d)
                await Task.Delay(d, ct);
            return result;
        }
    }

    private sealed class FakeRegistry(params IConsultationProvider[] providers) : IConsultationProviderRegistry
    {
        private readonly Dictionary<string, IConsultationProvider> _map =
            providers.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);

        public IConsultationProvider? Resolve(string providerKey) =>
            _map.GetValueOrDefault(providerKey);
    }
}
