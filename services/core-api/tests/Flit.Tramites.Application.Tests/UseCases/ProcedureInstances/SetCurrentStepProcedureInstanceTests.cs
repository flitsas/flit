using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #10879 — persistir el avance del borrador por pasos.
/// AC1: en un borrador con la consulta del vehículo completa, avanzar de paso persiste el paso.
/// AC2: al reabrir el borrador, el estado del wizard / DTO refleja el último avance persistido.
/// </summary>
public sealed class SetCurrentStepProcedureInstanceTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly SetCurrentStepProcedureInstanceHandler _sut;

    public SetCurrentStepProcedureInstanceTests() => _sut = new SetCurrentStepProcedureInstanceHandler(_repo);

    /// <summary>
    /// Borrador de matrícula. Con <paramref name="vehiculoConsultado"/> se hidrata la placa en
    /// field_values (misma señal que abre el paso 1 del wizard).
    /// </summary>
    private static ProcedureInstance Instance(
        Guid id, Guid tenant,
        string status = TramiteEstado.Borrador,
        string? currentStep = null,
        bool vehiculoConsultado = true)
    {
        var instance = new ProcedureInstance
        {
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            ModalidadEntrada = "matricula_inicial",
            CurrentStep = currentStep,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        if (vehiculoConsultado)
            instance.FieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                ProcedureInstanceId = id,
                FieldKey = "plate",
                ValueText = "ABC123",
                Source = "consultation",
            });

        return instance;
    }

    // ── AC1 — persistir el avance ─────────────────────────────────────────────────

    [Fact]
    public async Task Advance_OnDraftWithVehicleQuery_PersistsStep()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        _repo.GetByIdWithDetailsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(id, tenant, "documentos", ct);

        error.Should().BeNull();
        result!.CurrentStep.Should().Be("documentos");
        instance.CurrentStep.Should().Be("documentos");
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    // ── AC1 — rechazo si el trámite no está en borrador ───────────────────────────

    [Fact]
    public async Task Advance_OnNonDraft_ReturnsNotDraft_AndDoesNotPersist()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, status: TramiteEstado.Entregado);
        _repo.GetByIdWithDetailsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(id, tenant, "documentos", ct);

        result.Should().BeNull();
        error.Should().Be(SetCurrentStepProcedureInstanceHandler.NotDraft);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── AC1 — rechazo si la consulta del vehículo no está completa ────────────────

    [Fact]
    public async Task Advance_WithoutVehicleQuery_ReturnsVehiculoNoConsultado()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, vehiculoConsultado: false);
        _repo.GetByIdWithDetailsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(id, tenant, "documentos", ct);

        result.Should().BeNull();
        error.Should().Be(SetCurrentStepProcedureInstanceHandler.VehiculoNoConsultado);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Paso inválido / vacío ─────────────────────────────────────────────────────

    [Fact]
    public async Task Advance_WithUnknownStepKey_ReturnsStepInvalid()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        _repo.GetByIdWithDetailsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(id, tenant, "paso_inexistente", ct);

        result.Should().BeNull();
        error.Should().Be(SetCurrentStepProcedureInstanceHandler.StepInvalid);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Advance_WithEmptyStep_ReturnsStepInvalid_WithoutLoadingInstance()
    {
        var ct = TestContext.Current.CancellationToken;

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), "   ", ct);

        result.Should().BeNull();
        error.Should().Be(SetCurrentStepProcedureInstanceHandler.StepInvalid);
        await _repo.DidNotReceive().GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── Idempotencia ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Advance_ToSameStep_IsIdempotent_DoesNotPersist()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, currentStep: "comprador");
        _repo.GetByIdWithDetailsAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(id, tenant, "comprador", ct);

        error.Should().BeNull();
        result!.CurrentStep.Should().Be("comprador");
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── No encontrado ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Advance_NotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), "documentos", ct);

        result.Should().BeNull();
        error.Should().Be(SetCurrentStepProcedureInstanceHandler.NotFound);
    }

    // ── AC2 — al reabrir, el DTO de la instancia refleja el paso persistido ───────

    [Fact]
    public async Task Reopen_DetailDto_ReflectsPersistedCurrentStep()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, currentStep: "comprador");
        _repo.GetByIdWithDetailsAsync(id, tenant, ct).Returns(instance);

        var get = new GetProcedureInstanceHandler(_repo);
        var (detail, error) = await get.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        detail!.CurrentStep.Should().Be("comprador");
    }
}
