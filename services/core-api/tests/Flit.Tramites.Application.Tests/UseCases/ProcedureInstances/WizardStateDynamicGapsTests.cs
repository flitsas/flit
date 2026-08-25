using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// ADR-0050 — gaps del camino dinámico de <see cref="GetWizardStateHandler"/>.
/// <para>La bifurcación al motor dinámico ocurría <b>antes</b> de resolver identidad, RNMC, prenda del
/// OT y la matriz documental, así que devolvía los defaults del DTO: encender el flag degradaba el
/// wizard en lugar de mejorarlo. Estos tests fijan que el camino dinámico recibe las mismas señales
/// que el estático, y cubren el truncado a <c>sectionTypes[0]</c> y el <c>SectionConfig</c> que nunca
/// se asignaba.</para>
/// </summary>
public sealed class WizardStateDynamicGapsTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeSnapshotRepository _snapshots = Substitute.For<IProcedureTypeSnapshotRepository>();

    private sealed class StubDynamicPolicy(bool enabled) : IDynamicProceduresPolicy
    {
        public Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(enabled);
    }

    private sealed class StubIdentityPolicy(bool required) : IIdentityValidationPolicy
    {
        public Task<bool> IsIdentityValidationRequiredAsync(Guid tenantId, Guid? transitOfficeId, CancellationToken ct = default)
            => Task.FromResult(required);
    }

    private sealed class StubRnmcPolicy(bool required) : IRnmcRequirementPolicy
    {
        public Task<bool> IsRnmcRequiredAsync(Guid tenantId, Guid? transitOfficeId, CancellationToken ct = default)
            => Task.FromResult(required);
    }

    private sealed class StubMatrixProvider(params ResolvedChecklistDoc[] docs) : IResolvedChecklistMatrixProvider
    {
        public Task<IReadOnlyList<ResolvedChecklistDoc>> GetForAsync(
            Guid procedureTypeId, Guid? transitOfficeId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ResolvedChecklistDoc>>(docs);
    }

    private static ProcedureInstance Base() => new()
    {
        ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000042",
        Status = TramiteEstado.Borrador,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Snapshot realista: un paso con DOS secciones, no el ideal 1:1 de los tests previos.</summary>
    private const string SnapshotMultiSeccion = """
    {
      "code": "MATRICULA_NUEVA", "family": "MATRICULAS", "version": 1,
      "gateProfile": {
        "entryMode": "VIN", "requiresBuyer": true, "requiresBiometrics": true,
        "biometricActors": ["BUYER"], "requiresSignature": true
      },
      "conformationRules": [],
      "stepSectionTypes": [
        { "stepCode": "consulta", "sectionTypes": ["vehicle_query", "document_checklist"] },
        { "stepCode": "comprador", "sectionTypes": ["actor_form"] }
      ]
    }
    """;

    private (GetWizardStateHandler Handler, ProcedureInstance Instance) Armar(
        string snapshotJson = SnapshotMultiSeccion,
        bool identityRequired = true,
        bool rnmcRequired = false,
        IResolvedChecklistMatrixProvider? matrix = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base();
        _repo.GetByIdWithWizardGraphAsync(instance.Id, instance.TenantId, ct).Returns(instance);
        _snapshots.GetByInstanceIdAsync(instance.Id, instance.TenantId, ct).Returns(
            new ProcedureTypeSnapshotRecord(
                Guid.NewGuid(), instance.TenantId, instance.Id, instance.ProcedureTypeId, 1, snapshotJson, null));

        var handler = new GetWizardStateHandler(
            _repo,
            identityPolicy: new StubIdentityPolicy(identityRequired),
            rnmcPolicy: new StubRnmcPolicy(rnmcRequired),
            dynamicPolicy: new StubDynamicPolicy(true),
            snapshotRepo: _snapshots,
            checklistMatrixProvider: matrix);

        return (handler, instance);
    }

    [Fact]
    public async Task PasoConVariasSecciones_LasEvaluaTodas_YLasExpone()
    {
        // Antes solo sobrevivía sectionTypes[0]: el gate de documentos de ese paso no se evaluaba.
        var (handler, instance) = Armar();

        var (result, _) = await handler.HandleAsync(instance.Id, instance.TenantId, TestContext.Current.CancellationToken);

        var paso = result!.Steps[0];
        paso.SectionType.Should().Be(ProcedureSectionTypes.VehicleQuery, "la primera sección sigue siendo el renderer principal");
        paso.SectionTypes.Should().ContainInOrder(
            ProcedureSectionTypes.VehicleQuery, ProcedureSectionTypes.DocumentChecklist);
        paso.Reasons.Should().Contain(DynamicGateEvaluator.VehiculoNoConsultado);
        paso.Reasons.Should().Contain(DynamicGateEvaluator.DocumentosIncompletos,
            "la segunda sección del paso también aporta su razón");
    }

    [Fact]
    public async Task IdentityValidationEnabled_SePropagaDesdeLaPolitica()
    {
        var (handler, instance) = Armar(identityRequired: false);

        var (result, _) = await handler.HandleAsync(instance.Id, instance.TenantId, TestContext.Current.CancellationToken);

        result!.IdentityValidationEnabled.Should().BeFalse("antes el camino dinámico devolvía el default true");
    }

    [Fact]
    public async Task IdentidadDeshabilitadaPorElOt_NoBloqueaLaBiometria()
    {
        // El flag no basta: si el OT la deshabilita, las partes cuentan como satisfechas y el gate
        // biométrico no debe aportar blocker (HU #10548).
        var (handler, instance) = Armar(identityRequired: false);

        var (result, _) = await handler.HandleAsync(instance.Id, instance.TenantId, TestContext.Current.CancellationToken);

        result!.Blockers.Should().NotContain(DynamicGateEvaluator.IdentidadNoAprobada);
    }

    [Fact]
    public async Task RnmcEnabled_SePropagaDesdeLaPolitica()
    {
        var (handler, instance) = Armar(rnmcRequired: true);

        var (result, _) = await handler.HandleAsync(instance.Id, instance.TenantId, TestContext.Current.CancellationToken);

        result!.RnmcEnabled.Should().BeTrue("antes el camino dinámico devolvía el default false");
    }

    [Fact]
    public async Task MatrizDocumental_ProduceBlockersPorDocumento()
    {
        // CFD-06 llegaba implementado y testeado en el gate, pero nadie poblaba DocumentRequirements.
        var matrix = new StubMatrixProvider(
            new ResolvedChecklistDoc("FACTURA", "Factura", Obligatorio: true, Orden: 1),
            new ResolvedChecklistDoc("ADUANA", "Manifiesto", Obligatorio: false, Orden: 2));
        var (handler, instance) = Armar(matrix: matrix);

        var (result, _) = await handler.HandleAsync(instance.Id, instance.TenantId, TestContext.Current.CancellationToken);

        result!.Blockers.Should().Contain(DocumentRequirementGate.BlockerFor("FACTURA"));
        result.Blockers.Should().NotContain(DocumentRequirementGate.BlockerFor("ADUANA"),
            "un documento no obligatorio no bloquea");
    }

    [Fact]
    public async Task DocumentoDummy_NoBloquea_AunqueSeaObligatorio()
    {
        var matrix = new StubMatrixProvider(
            new ResolvedChecklistDoc("BUZON", "Buzón informativo", Obligatorio: true, Orden: 1, EsDummy: true));
        var (handler, instance) = Armar(matrix: matrix);

        var (result, _) = await handler.HandleAsync(instance.Id, instance.TenantId, TestContext.Current.CancellationToken);

        result!.Blockers.Should().NotContain(DocumentRequirementGate.BlockerFor("BUZON"));
    }

    [Fact]
    public async Task SectionConfig_SeAsigna_ConLasCapacidadesDelTipo()
    {
        // La propiedad existía en el contrato desde F08 y jamás se asignaba.
        var (handler, instance) = Armar();

        var (result, _) = await handler.HandleAsync(instance.Id, instance.TenantId, TestContext.Current.CancellationToken);

        var consulta = result!.Steps[0];
        consulta.SectionConfig.Should().NotBeNull();
        consulta.SectionConfig!["entryMode"]!.GetValue<string>().Should().Be("VIN");

        var actores = result.Steps[1];
        actores.SectionConfig!["requiresBuyer"]!.GetValue<bool>().Should().BeTrue();
        actores.SectionConfig!["requiresSeller"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void Comparendos_BloqueanSoloSiLaCompaniaLosMarcoBloqueantes()
    {
        // Paridad con TraspasoGates (FEATURE 05). Se prueba sobre el evaluador puro: la señal SIMIT
        // se extrae del preflight en el handler, y aquí interesa la regla, no la extracción.
        var profile = new ProcedureTypeGateProfile { RequiresBuyer = true };
        List<DynamicWizardStep> steps = [new("comprador", ProcedureSectionTypes.ActorForm)];

        var bloqueante = DynamicGateEvaluator.Evaluate(profile, steps, new DynamicWizardContext
        {
            HasBuyer = true,
            BuyerRuntConsultado = true,
            CompradorConComparendos = true,
            ComparendosBloquean = true,
        });

        var informativo = DynamicGateEvaluator.Evaluate(profile, steps, new DynamicWizardContext
        {
            HasBuyer = true,
            BuyerRuntConsultado = true,
            CompradorConComparendos = true,
            ComparendosBloquean = false,
        });

        bloqueante.Steps[0].Reasons.Should().Contain(DynamicGateEvaluator.SimitMultas);
        informativo.Steps[0].Status.Should().Be("complete");
    }
}
