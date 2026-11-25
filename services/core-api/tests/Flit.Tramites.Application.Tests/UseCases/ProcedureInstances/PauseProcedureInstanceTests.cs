using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Pausar/reanudar trámites ICT desde la UI de FLIT (paridad v1). Solo borradores origin='ict':
/// individual + masivo. La radicación bloqueada se cubre en <see cref="SubmitProcedureInstanceTests"/>.
/// HU #14668: semántica de observation (null=keep, non-null incluso vacío=actualizar).
/// </summary>
public sealed class PauseProcedureInstanceTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly PauseProcedureInstanceHandler _sut;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public PauseProcedureInstanceTests() => _sut = new PauseProcedureInstanceHandler(_repo);

    private static ProcedureInstance Instance(
        Guid id, Guid tenantId, string status = TramiteEstado.Borrador, string? origin = "ict") =>
        new()
        {
            ProcedureType = ProcedureTypeFixture.For("traspaso" ?? "traspaso_estandar"),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            Origin = origin,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // ── Pausa/reanudación individual ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pause_ictBorrador_setsFlagObservationAndSaves()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var changedBy = Guid.NewGuid();
        var (ok, error, _) = await _sut.HandleAsync(id, tenantId, paused: true, "cliente pidió esperar", changedBy, Ct);

        ok.Should().BeTrue();
        error.Should().BeNull();
        instance.IsPaused.Should().BeTrue();
        instance.PausedObservation.Should().Be("cliente pidió esperar");
        await _repo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e =>
                e.Tipo == "tramite_pausado"
                && e.ProcedureInstanceId == id
                && e.CreatedBy == changedBy),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>HU #14668 §2 — reanudar con observation null → keep: PausedObservation NO se borra.</summary>
    [Fact]
    public async Task Resume_withNullObservation_keepsExistingPausedObservation()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId);
        instance.IsPaused = true;
        instance.PausedObservation = "vieja obs";
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (ok, error, _) = await _sut.HandleAsync(id, tenantId, paused: false, observation: null, changedBy: null, Ct);

        ok.Should().BeTrue();
        error.Should().BeNull();
        instance.IsPaused.Should().BeFalse();
        // null = keep → PausedObservation conserva "vieja obs"
        instance.PausedObservation.Should().Be("vieja obs");
        await _repo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e => e.Tipo == "tramite_reanudado"), Arg.Any<CancellationToken>());
    }

    /// <summary>HU #14668 §2 — reanudar con observation "" → Truncate("") = null → borra PausedObservation.</summary>
    [Fact]
    public async Task Resume_withEmptyObservation_clearsPausedObservation()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId);
        instance.IsPaused = true;
        instance.PausedObservation = "vieja obs";
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (ok, error, _) = await _sut.HandleAsync(id, tenantId, paused: false, observation: "", changedBy: null, Ct);

        ok.Should().BeTrue();
        error.Should().BeNull();
        instance.IsPaused.Should().BeFalse();
        // empty string → Truncate → null
        instance.PausedObservation.Should().BeNull();
    }

    /// <summary>HU #14668 §2 — pausar con observation null → keep (no tocar PausedObservation existente).</summary>
    [Fact]
    public async Task Pause_withNullObservation_keepsExistingPausedObservation()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId);
        instance.PausedObservation = "obs anterior";
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (ok, error, _) = await _sut.HandleAsync(id, tenantId, paused: true, observation: null, changedBy: null, Ct);

        ok.Should().BeTrue();
        instance.IsPaused.Should().BeTrue();
        instance.PausedObservation.Should().Be("obs anterior"); // keep
    }

    /// <summary>HU #14668 §2 — reanudar con observation nueva → actualiza PausedObservation.</summary>
    [Fact]
    public async Task Resume_withNewObservation_updatesPausedObservation()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId);
        instance.IsPaused = true;
        instance.PausedObservation = "original";
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (ok, error, _) = await _sut.HandleAsync(id, tenantId, paused: false, observation: "motivo reanudación", changedBy: null, Ct);

        ok.Should().BeTrue();
        instance.IsPaused.Should().BeFalse();
        instance.PausedObservation.Should().Be("motivo reanudación");
    }

    [Fact]
    public async Task Pause_nonIct_returnsNotIctWithoutSaving()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(id, tenantId, origin: null)); // trámite de plataforma

        var (ok, error, _) = await _sut.HandleAsync(id, tenantId, paused: true, "obs", changedBy: null, Ct);

        ok.Should().BeFalse();
        error.Should().Be("not_ict");
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pause_notBorrador_returnsNotBorrador()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(id, tenantId, status: TramiteEstado.Entregado));

        var (ok, error, _) = await _sut.HandleAsync(id, tenantId, paused: true, "obs", changedBy: null, Ct);

        ok.Should().BeFalse();
        error.Should().Be("not_borrador");
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pause_notFound_returnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstance?)null);

        var (ok, error, _) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), paused: true, "obs", null, Ct);

        ok.Should().BeFalse();
        error.Should().Be("not_found");
    }

    // ── Pausa/reanudación masiva ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Bulk_mixedResults_savesOnceWithPerIdDetail()
    {
        var tenantId = Guid.NewGuid();
        var okId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var platformId = Guid.NewGuid();
        _repo.GetByIdAsync(okId, tenantId, Arg.Any<CancellationToken>()).Returns(Instance(okId, tenantId));
        _repo.GetByIdAsync(missingId, tenantId, Arg.Any<CancellationToken>()).Returns((ProcedureInstance?)null);
        _repo.GetByIdAsync(platformId, tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(platformId, tenantId, origin: null));

        var command = new PauseProcedureInstancesBulkCommand(
            Ids: [okId, missingId, platformId],
            Paused: true,
            Observation: "obs");

        var results = await _sut.HandleBulkAsync(command, tenantId, changedBy: null, Ct);

        results.Should().HaveCount(3);
        results.Single(r => r.Id == okId).Ok.Should().BeTrue();
        results.Single(r => r.Id == missingId).Error.Should().Be("not_found");
        results.Single(r => r.Id == platformId).Error.Should().Be("not_ict");
        // Al menos uno aplicó → un solo SaveChanges para todo el lote.
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bulk_noneApplied_doesNotSave()
    {
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(Arg.Any<Guid>(), tenantId, Arg.Any<CancellationToken>())
            .Returns((ProcedureInstance?)null);

        var command = new PauseProcedureInstancesBulkCommand(
            Ids: [Guid.NewGuid(), Guid.NewGuid()],
            Paused: false,
            Observation: null);

        var results = await _sut.HandleBulkAsync(command, tenantId, changedBy: null, Ct);

        results.Should().OnlyContain(r => !r.Ok);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>HU #14668 §1 — modo keep: PausedObservation no se toca, solo is_paused cambia.</summary>
    [Fact]
    public async Task Bulk_modeKeep_doesNotTouchPausedObservation()
    {
        var tenantId = Guid.NewGuid();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var instance1 = Instance(id1, tenantId);
        var instance2 = Instance(id2, tenantId);
        instance1.PausedObservation = "original 1";
        instance2.PausedObservation = "original 2";
        _repo.GetByIdAsync(id1, tenantId, Arg.Any<CancellationToken>()).Returns(instance1);
        _repo.GetByIdAsync(id2, tenantId, Arg.Any<CancellationToken>()).Returns(instance2);

        var command = new PauseProcedureInstancesBulkCommand(
            Ids: [id1, id2],
            Paused: true,
            ObservationMode: BulkObservationMode.Keep);

        await _sut.HandleBulkAsync(command, tenantId, changedBy: null, Ct);

        instance1.IsPaused.Should().BeTrue();
        instance1.PausedObservation.Should().Be("original 1"); // keep
        instance2.PausedObservation.Should().Be("original 2"); // keep
    }

    /// <summary>HU #14668 §1 — modo single (SameForAll): misma observation para todos.</summary>
    [Fact]
    public async Task Bulk_modeSingle_appliesSameObservationToAll()
    {
        var tenantId = Guid.NewGuid();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var instance1 = Instance(id1, tenantId);
        var instance2 = Instance(id2, tenantId);
        _repo.GetByIdAsync(id1, tenantId, Arg.Any<CancellationToken>()).Returns(instance1);
        _repo.GetByIdAsync(id2, tenantId, Arg.Any<CancellationToken>()).Returns(instance2);

        var command = new PauseProcedureInstancesBulkCommand(
            Ids: [id1, id2],
            Paused: true,
            ObservationMode: BulkObservationMode.SameForAll,
            Observation: "pausa por auditoria");

        await _sut.HandleBulkAsync(command, tenantId, changedBy: null, Ct);

        instance1.PausedObservation.Should().Be("pausa por auditoria");
        instance2.PausedObservation.Should().Be("pausa por auditoria");
    }

    /// <summary>HU #14668 §1 — modo perItem: cada trámite recibe su observación individual.</summary>
    [Fact]
    public async Task Bulk_modePerItem_appliesIndividualObservations()
    {
        var tenantId = Guid.NewGuid();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        var instance1 = Instance(id1, tenantId);
        var instance2 = Instance(id2, tenantId);
        var instance3 = Instance(id3, tenantId);
        instance3.PausedObservation = "obs previa";
        _repo.GetByIdAsync(id1, tenantId, Arg.Any<CancellationToken>()).Returns(instance1);
        _repo.GetByIdAsync(id2, tenantId, Arg.Any<CancellationToken>()).Returns(instance2);
        _repo.GetByIdAsync(id3, tenantId, Arg.Any<CancellationToken>()).Returns(instance3);

        var command = new PauseProcedureInstancesBulkCommand(
            Ids: [id1, id2, id3],
            Paused: true,
            ObservationMode: BulkObservationMode.PerItem,
            Items:
            [
                new BulkPauseItemCommand(id1, "obs para id1"),
                new BulkPauseItemCommand(id2, null), // sin item → null = keep
                // id3 no está en items → null = keep (obs previa se conserva)
            ]);

        await _sut.HandleBulkAsync(command, tenantId, changedBy: null, Ct);

        instance1.PausedObservation.Should().Be("obs para id1");
        instance2.PausedObservation.Should().BeNull(); // null = keep pero era null → sigue null
        instance3.PausedObservation.Should().Be("obs previa"); // id3 no en items → keep
    }

    /// <summary>HU #14668 §1 — backward-compat: sin mode con observation → behave as single.</summary>
    [Fact]
    public async Task Bulk_noMode_withObservation_behavesAsSingle()
    {
        var tenantId = Guid.NewGuid();
        var id1 = Guid.NewGuid();
        var instance1 = Instance(id1, tenantId);
        _repo.GetByIdAsync(id1, tenantId, Arg.Any<CancellationToken>()).Returns(instance1);

        // Sin ObservationMode, con Observation → backward-compat = single
        var command = new PauseProcedureInstancesBulkCommand(
            Ids: [id1],
            Paused: true,
            Observation: "obs global");

        await _sut.HandleBulkAsync(command, tenantId, changedBy: null, Ct);

        instance1.PausedObservation.Should().Be("obs global");
    }

    /// <summary>HU #14668 §1 — backward-compat: sin mode sin observation → behave as keep.</summary>
    [Fact]
    public async Task Bulk_noMode_noObservation_behavesAsKeep()
    {
        var tenantId = Guid.NewGuid();
        var id1 = Guid.NewGuid();
        var instance1 = Instance(id1, tenantId);
        instance1.PausedObservation = "conservar";
        _repo.GetByIdAsync(id1, tenantId, Arg.Any<CancellationToken>()).Returns(instance1);

        // Sin ObservationMode, sin Observation → backward-compat = keep
        var command = new PauseProcedureInstancesBulkCommand(
            Ids: [id1],
            Paused: true,
            Observation: null);

        await _sut.HandleBulkAsync(command, tenantId, changedBy: null, Ct);

        instance1.PausedObservation.Should().Be("conservar"); // keep
    }
}
