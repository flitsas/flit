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
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            ModalidadEntrada = "traspaso_estandar",
            TipologiaCodigo = "traspaso",
            Origin = origin,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task Pause_ictBorrador_setsFlagObservationAndSaves()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId);
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (ok, error) = await _sut.HandleAsync(id, tenantId, paused: true, "cliente pidió esperar", Guid.NewGuid(), Ct);

        ok.Should().BeTrue();
        error.Should().BeNull();
        instance.IsPaused.Should().BeTrue();
        instance.PausedObservation.Should().Be("cliente pidió esperar");
        await _repo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e => e.Tipo == "tramite_pausado" && e.ProcedureInstanceId == id),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resume_clearsObservation()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId);
        instance.IsPaused = true;
        instance.PausedObservation = "vieja obs";
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (ok, error) = await _sut.HandleAsync(id, tenantId, paused: false, observation: null, changedBy: null, Ct);

        ok.Should().BeTrue();
        error.Should().BeNull();
        instance.IsPaused.Should().BeFalse();
        instance.PausedObservation.Should().BeNull();
        await _repo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e => e.Tipo == "tramite_reanudado"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pause_nonIct_returnsNotIctWithoutSaving()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns(Instance(id, tenantId, origin: null)); // trámite de plataforma

        var (ok, error) = await _sut.HandleAsync(id, tenantId, paused: true, "obs", changedBy: null, Ct);

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

        var (ok, error) = await _sut.HandleAsync(id, tenantId, paused: true, "obs", changedBy: null, Ct);

        ok.Should().BeFalse();
        error.Should().Be("not_borrador");
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pause_notFound_returnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstance?)null);

        var (ok, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), paused: true, "obs", null, Ct);

        ok.Should().BeFalse();
        error.Should().Be("not_found");
    }

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

        var results = await _sut.HandleBulkAsync(
            [okId, missingId, platformId], tenantId, paused: true, "obs", changedBy: null, Ct);

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

        var results = await _sut.HandleBulkAsync(
            [Guid.NewGuid(), Guid.NewGuid()], tenantId, paused: false, null, null, Ct);

        results.Should().OnlyContain(r => !r.Ok);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
