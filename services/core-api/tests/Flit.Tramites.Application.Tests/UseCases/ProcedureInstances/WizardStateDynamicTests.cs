using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// FEATURE-08 / HU-BE-06 (CFD-09) — WizardStateQuery flag-guarded. Cubre BE-06-AC-01 (flag=true usa
/// DynamicGateEvaluator y devuelve sectionType), AC-02 (flag=false conserva el camino estático),
/// AC-05 (WizardStepDto incluye sectionType).
/// </summary>
public sealed class WizardStateDynamicTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeSnapshotRepository _snapshots = Substitute.For<IProcedureTypeSnapshotRepository>();

    private sealed class StubDynamicPolicy(bool enabled) : IDynamicProceduresPolicy
    {
        public Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(enabled);
    }

    private static ProcedureInstance Base(string modalidad = "matricula_inicial") => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000001",
        Status = TramiteEstado.Borrador,
        ModalidadEntrada = modalidad,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private const string SnapshotJson = """
    {
      "code": "MATRICULA_NUEVA", "family": "matriculas", "version": 1,
      "gateProfile": { "entryMode": "VIN", "requiresBuyer": true },
      "conformationRules": [],
      "stepSectionTypes": [
        { "stepCode": "consulta", "sectionTypes": ["vehicle_query"] },
        { "stepCode": "documentos", "sectionTypes": ["document_checklist"] }
      ]
    }
    """;

    [Fact]
    public async Task FlagEnabled_WithSnapshot_UsesDynamicPathWithSectionTypes()
    {
        // BE-06-AC-01 / AC-05
        var ct = TestContext.Current.CancellationToken;
        var instance = Base();
        _repo.GetByIdWithWizardGraphAsync(instance.Id, instance.TenantId, ct).Returns(instance);
        _snapshots.GetByInstanceIdAsync(instance.Id, instance.TenantId, ct).Returns(
            new ProcedureTypeSnapshotRecord(
                Guid.NewGuid(), instance.TenantId, instance.Id, instance.ProcedureTypeId, 1, SnapshotJson, null));

        var handler = new GetWizardStateHandler(_repo, dynamicPolicy: new StubDynamicPolicy(true), snapshotRepo: _snapshots);
        var (result, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.TotalSteps.Should().Be(2);
        result.Steps.Select(s => s.SectionType).Should().ContainInOrder("vehicle_query", "document_checklist");
        result.Steps[0].Reasons.Should().Contain(DynamicGateEvaluator_VehiculoNoConsultado());
    }

    [Fact]
    public async Task FlagDisabled_UsesStaticPath_NoSectionType()
    {
        // BE-06-AC-02: sin regresión — camino estático (BuildMatricula = 5 pasos, sin sectionType).
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        _repo.GetByIdWithWizardGraphAsync(instance.Id, instance.TenantId, ct).Returns(instance);

        var handler = new GetWizardStateHandler(_repo, dynamicPolicy: new StubDynamicPolicy(false), snapshotRepo: _snapshots);
        var (result, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.TotalSteps.Should().Be(5); // matrícula estática
        result.Steps.Should().OnlyContain(s => s.SectionType == null);
        await _snapshots.DidNotReceive().GetByInstanceIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FlagEnabled_ButNoSnapshot_FallsBackToStaticPath()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Base("matricula_inicial");
        _repo.GetByIdWithWizardGraphAsync(instance.Id, instance.TenantId, ct).Returns(instance);
        _snapshots.GetByInstanceIdAsync(instance.Id, instance.TenantId, ct).Returns((ProcedureTypeSnapshotRecord?)null);

        var handler = new GetWizardStateHandler(_repo, dynamicPolicy: new StubDynamicPolicy(true), snapshotRepo: _snapshots);
        var (result, error) = await handler.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.TotalSteps.Should().Be(5); // sin snapshot → estático
    }

    private static string DynamicGateEvaluator_VehiculoNoConsultado() =>
        Flit.Tramites.Domain.Tramites.Services.DynamicGateEvaluator.VehiculoNoConsultado;
}
