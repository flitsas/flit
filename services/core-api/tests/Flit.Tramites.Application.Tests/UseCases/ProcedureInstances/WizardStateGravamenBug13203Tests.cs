using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #13203 — el gate de prenda (<c>Capabilities.HasPrendaGate</c>) lee la señal única de gravamen:
/// banderas RUNT afirmativas O <c>runt_gravamenes</c> con al menos una garantía.
/// Uso de ejemplo:
/// <code>var (dto, _) = await new GetWizardStateHandler(repo).HandleAsync(id, tenant, ct); dto!.Capabilities!.HasPrendaGate</code>
/// </summary>
public sealed class WizardStateGravamenBug13203Tests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    private static ProcedureInstance Matricula(params (string Key, string? Text, string? Json)[] fv)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013203",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var (key, text, json) in fv)
        {
            instance.FieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                TenantId = instance.TenantId,
                ProcedureInstanceId = instance.Id,
                FieldKey = key,
                ValueText = text,
                ValueJson = json,
                Source = "consulta",
            });
        }

        return instance;
    }

    private async Task<bool> HasPrendaGate(ProcedureInstance instance)
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithWizardGraphAsync(instance.Id, instance.TenantId, ct).Returns(instance);
        var (result, error) = await new GetWizardStateHandler(_repo).HandleAsync(instance.Id, instance.TenantId, ct);
        error.Should().BeNull();
        return result!.Capabilities!.HasPrendaGate;
    }

    [Fact]
    public async Task BanderasNo_ConGarantiaEnRuntGravamenes_ExigeDecisionDePrenda()
    {
        var instance = Matricula(
            ("runt_tiene_prendas", "NO", null),
            ("runt_tiene_gravamenes", "NO", null),
            ("runt_gravamenes", null, """[{"idPrenda":"1000001","nombreAcreedor":"BANCO DE PRUEBA S.A."}]"""));

        (await HasPrendaGate(instance)).Should().BeTrue();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{roto")]
    public async Task BanderasNo_ConDetalleVacioOInvalido_NoExigeDecisionDePrenda(string detalle)
    {
        var instance = Matricula(
            ("runt_tiene_prendas", "NO", null),
            ("runt_tiene_gravamenes", "NO", null),
            ("runt_gravamenes", null, detalle));

        (await HasPrendaGate(instance)).Should().BeFalse();
    }

    [Fact]
    public async Task Control_BanderaSi_ExigeDecisionDePrenda()
    {
        (await HasPrendaGate(Matricula(("runt_tiene_prendas", "SI", null)))).Should().BeTrue();
    }

    // ── Revisión PR #504 (B1): la re-consulta sin garantías pisa el detalle anterior ─────────────

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

    [Fact]
    public async Task ReConsulta_SinGarantias_PisaElDetalleAnterior_YApagaElGate()
    {
        var ct = TestContext.Current.CancellationToken;
        // Consulta anterior: banderas SI y una garantía guardada.
        var instance = Matricula(
            ("vin", "1HGCM82633A004352", null),
            ("runt_tiene_prendas", "SI", null),
            ("runt_tiene_gravamenes", "SI", null),
            ("runt_nombre_acreedor", "BANCO DE PRUEBA S.A.", null),
            ("runt_gravamenes", null, """[{"idPrenda":"1000001","nombreAcreedor":"BANCO DE PRUEBA S.A."}]"""));
        (await HasPrendaGate(instance)).Should().BeTrue("precondición: la consulta anterior dejó gravamen");

        // Re-consulta Verifik: el RUNT ya no reporta nada (banderas ausentes, sin garantías). Estado
        // INACTIVO: en matrícula «ACTIVO» es el bloqueo CF-03 y un estado ausente también bloquea
        // (unknown); ninguno de los dos es lo que se prueba aquí.
        const string json = """
            { "data": { "informacionGeneral": { "noVin": "1HGCM82633A004352", "estadoDelVehiculo": "INACTIVO" },
                        "soat": [], "tecnoMecanica": [], "garantiasMobiliarias": [] } }
            """;
        var reconsulta = VerifikResultMapper.MapVehicle(JsonSerializer.Deserialize<VerifikVehicleResponse>(json)!);
        var registry = new StaticRegistry(new Dictionary<string, IConsultationProvider>
        {
            ["verifik"] = new StubProvider("verifik", reconsulta),
        });
        var preflight = new RunPreflightHandler(
            _repo, registry, new ConsultationProviderChainResolver(registry, new ConsultationChainOptions()),
            new NullOverrideProvider(), NullConsultationRestrictionPolicy.Instance, NullTransitOfficeResolver.Instance);

        var (_, error, _, _) = await preflight.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        instance.FieldValues.Single(f => f.FieldKey == "runt_gravamenes").ValueJson.Should().Be("[]");
        instance.FieldValues.Single(f => f.FieldKey == "runt_nombre_acreedor").ValueText.Should().BeNull();
        instance.FieldValues.Single(f => f.FieldKey == "runt_tiene_prendas").ValueText.Should().BeNull();
        instance.FieldValues.Single(f => f.FieldKey == "runt_tiene_gravamenes").ValueText.Should().BeNull();
        RuntGravamenSignal.Reporta(instance.FieldValues).Should().BeFalse();
        (await HasPrendaGate(instance)).Should().BeFalse();
    }
}
