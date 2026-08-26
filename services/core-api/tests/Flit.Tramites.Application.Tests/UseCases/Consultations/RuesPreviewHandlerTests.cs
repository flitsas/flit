using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Consultations;

/// <summary>
/// HU sin ADO 2026-08-11 (segunda tanda) — <see cref="RuesPreviewHandler"/>, la consulta RUES SIN
/// instancia (paso 1, casilla 19 del FUR). No persiste nada, así que a diferencia de
/// <see cref="RuesPersonLookupHandlerTests"/> no hay repositorio ni caché que verificar: solo la
/// resolución del proveedor y el mapeo del resultado.
/// </summary>
public sealed class RuesPreviewHandlerTests
{
    private readonly IConsultationProviderRegistry _registry = Substitute.For<IConsultationProviderRegistry>();
    private readonly RuesPreviewHandler _sut;

    private sealed class FakeProvider(ConsultationResult result) : IConsultationProvider
    {
        public string Key => "verifik_rues";
        public ConsultationContext? LastContext { get; private set; }

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
        {
            LastContext = ctx;
            return Task.FromResult(result);
        }
    }

    public RuesPreviewHandlerTests()
    {
        _sut = new RuesPreviewHandler(_registry);
    }

    [Fact]
    public async Task HandleAsync_InvalidRequest_WhenBlankDocument()
    {
        var ct = TestContext.Current.CancellationToken;

        var (result, error) = await _sut.HandleAsync("   ", Guid.NewGuid(), ct);

        error.Should().Be("invalid_request");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ProviderNotFound_WhenRegistryReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        _registry.Resolve("verifik_rues").Returns((IConsultationProvider?)null);

        var (result, error) = await _sut.HandleAsync("900123456", Guid.NewGuid(), ct);

        error.Should().Be("provider_not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_Found_ReturnsRazonSocial_SinInstancia()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var providerResult = new ConsultationResult("verifik_rues", "green", [],
            [
                new HydratedField("rues_razon_social", "ACME S.A.S.", null),
                new HydratedField("rues_estado", "ACTIVA", null),
            ]);
        var provider = new FakeProvider(providerResult);
        _registry.Resolve("verifik_rues").Returns(provider);

        var (result, error) = await _sut.HandleAsync("900123456", tenantId, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Found.Should().BeTrue();
        result.Nit.Should().Be("900123456");
        result.RazonSocial.Should().Be("ACME S.A.S.");
        // NO hay trámite: mismo convenio "sin instancia" que RunPreflightPreviewHandler.
        provider.LastContext!.InstanceId.Should().Be(Guid.Empty);
        provider.LastContext!.TenantId.Should().Be(tenantId);
        provider.LastContext!.TemplateCode.Should().Be("RUES_ACTOR_JURIDICAL");
    }

    [Fact]
    public async Task HandleAsync_NotFound_RazonSocialVaciaODesconocida_DevuelveFoundFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var providerResult = new ConsultationResult("verifik_rues", "unknown", [], []);
        _registry.Resolve("verifik_rues").Returns(new FakeProvider(providerResult));

        var (result, error) = await _sut.HandleAsync("999999999", Guid.NewGuid(), ct);

        error.Should().BeNull();
        result!.Found.Should().BeFalse();
        result.RazonSocial.Should().BeNull();
        result.Nit.Should().Be("999999999");
    }

    /// <summary>
    /// El fallo del proveedor (no-200, timeout, red, JSON ilegible) llega con CERO campos hidratados,
    /// exactamente igual que "esa empresa no existe". Mirar solo la razón social los confundía y le
    /// decía al operador que su NIT no existe cuando el servicio estaba caído — con un NIT real y
    /// verificable, que es como se detectó. La diferencia está en los checks del proveedor.
    /// </summary>
    [Fact]
    public async Task HandleAsync_FalloDelProveedor_NoSeConfundeConNitInexistente()
    {
        var ct = TestContext.Current.CancellationToken;
        var providerResult = new ConsultationResult("verifik_rues", "red",
            [
                new ConsultationCheck("provider", "Consulta RUES", "error", "verifik_rues",
                    "No fue posible verificar la información en RUES en este momento."),
            ],
            []);
        _registry.Resolve("verifik_rues").Returns(new FakeProvider(providerResult));

        var (result, error) = await _sut.HandleAsync("890903938", Guid.NewGuid(), ct);

        error.Should().Be("provider_unavailable");
        result.Should().BeNull();
    }

    /// <summary>
    /// El caso simétrico: el proveedor SÍ respondió y la empresa no existe (check "unknown", no
    /// "error"). Eso sí es un NIT a corregir, y debe seguir devolviendo 200 con found:false.
    /// </summary>
    [Fact]
    public async Task HandleAsync_NitInexistente_ConCheckUnknown_SigueSiendoFoundFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var providerResult = new ConsultationResult("verifik_rues", "yellow",
            [
                new ConsultationCheck("rues", "Consulta RUES", "unknown", "verifik_rues",
                    "No se encontró la empresa con NIT 999999999 en RUES"),
            ],
            []);
        _registry.Resolve("verifik_rues").Returns(new FakeProvider(providerResult));

        var (result, error) = await _sut.HandleAsync("999999999", Guid.NewGuid(), ct);

        error.Should().BeNull();
        result!.Found.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_TrimeaElNit()
    {
        var ct = TestContext.Current.CancellationToken;
        var providerResult = new ConsultationResult("verifik_rues", "unknown", [], []);
        _registry.Resolve("verifik_rues").Returns(new FakeProvider(providerResult));

        var (result, _) = await _sut.HandleAsync("  900123456  ", Guid.NewGuid(), ct);

        result!.Nit.Should().Be("900123456");
    }
}
