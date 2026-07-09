using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #10536 — marcar/desmarcar un trámite como prioritario. El flag solo afecta el ordenamiento de
/// los listados (no el ciclo de vida): se puede cambiar en cualquier estado y es idempotente.
/// </summary>
public sealed class SetPriorityProcedureInstanceTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly SetPriorityProcedureInstanceHandler _sut;

    public SetPriorityProcedureInstanceTests() => _sut = new SetPriorityProcedureInstanceHandler(_repo);

    private static ProcedureInstance Instance(
        Guid id, Guid tenant, bool prioritario = false, string status = TramiteEstado.Borrador) =>
        new()
        {
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            Prioritario = prioritario,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // ── AC1 — marcar prioritario ─────────────────────────────────────────────────

    [Fact]
    public async Task Mark_SetsPrioritarioTrue_AndPersists()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, prioritario: false);
        _repo.GetByIdAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(id, tenant, prioritario: true, ct);

        error.Should().BeNull();
        result!.Prioritario.Should().BeTrue();
        instance.Prioritario.Should().BeTrue();
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    /// <summary>El flag no depende del estado: un trámite ya entregado también puede priorizarse para la bandeja del OT.</summary>
    [Fact]
    public async Task Mark_OnSubmittedInstance_IsAllowed()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, prioritario: false, status: TramiteEstado.Entregado);
        _repo.GetByIdAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(id, tenant, prioritario: true, ct);

        error.Should().BeNull();
        result!.Prioritario.Should().BeTrue();
        instance.Status.Should().Be(TramiteEstado.Entregado); // no se altera el ciclo de vida
    }

    // ── AC2 — desmarcar prioridad ─────────────────────────────────────────────────

    [Fact]
    public async Task Unmark_SetsPrioritarioFalse_AndPersists()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, prioritario: true);
        _repo.GetByIdAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(id, tenant, prioritario: false, ct);

        error.Should().BeNull();
        result!.Prioritario.Should().BeFalse();
        instance.Prioritario.Should().BeFalse();
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    // ── Idempotencia ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SameValue_IsIdempotent_DoesNotPersist()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant, prioritario: true);
        _repo.GetByIdAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(id, tenant, prioritario: true, ct);

        error.Should().BeNull();
        result!.Prioritario.Should().BeTrue();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── No encontrado ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), prioritario: true, ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
    }
}
