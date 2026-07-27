using System.Text.Json;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;

public sealed class TramiteTransitionRecorderTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly TramiteTransitionRecorder _recorder;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset ChangedAt = DateTimeOffset.UtcNow;

    public TramiteTransitionRecorderTests()
    {
        _recorder = new TramiteTransitionRecorder(_repo);
    }

    private static TramiteTransitionRecord Record(Guid? changedBy = null, string? reason = "motivo x") =>
        new(TenantId, InstanceId, TramiteEstado.Borrador, TramiteEstado.Preparado, reason, changedBy, ChangedAt);

    [Fact]
    public async Task RecordAsync_WritesExactlyOneHistoryRowWithFromToUserAndReason()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.UserExistsAsync(UserId, ct).Returns(true);

        await _recorder.RecordAsync(Record(changedBy: UserId), ct);

        _repo.Received(1).Add(Arg.Is<ProcedureInstanceStatusHistory>(h =>
            h.TenantId == TenantId
            && h.ProcedureInstanceId == InstanceId
            && h.FromStatus == TramiteEstado.Borrador
            && h.ToStatus == TramiteEstado.Preparado
            && h.ChangedAt == ChangedAt
            && h.ChangedBy == UserId
            && h.Reason == "motivo x"));
    }

    [Fact]
    public async Task RecordAsync_WritesExactlyOneTimelineEventWithPayload()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.UserExistsAsync(UserId, ct).Returns(true);
        ProcedureInstanceEvent? evt = null;
        await _repo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(e => evt = e), Arg.Any<CancellationToken>());

        await _recorder.RecordAsync(Record(changedBy: UserId), ct);

        await _repo.Received(1).AddEventAsync(Arg.Any<ProcedureInstanceEvent>(), Arg.Any<CancellationToken>());
        evt.Should().NotBeNull();
        evt!.Tipo.Should().Be(TramiteTransitionRecorder.EventoTipo);
        evt.TenantId.Should().Be(TenantId);
        evt.ProcedureInstanceId.Should().Be(InstanceId);
        evt.CreatedAt.Should().Be(ChangedAt);
        evt.CreatedBy.Should().Be(UserId);

        using var payload = JsonDocument.Parse(evt.Payload);
        payload.RootElement.GetProperty("from_status").GetString().Should().Be(TramiteEstado.Borrador);
        payload.RootElement.GetProperty("to_status").GetString().Should().Be(TramiteEstado.Preparado);
        payload.RootElement.GetProperty("reason").GetString().Should().Be("motivo x");
    }

    [Fact]
    public async Task RecordAsync_DoesNotSaveChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.UserExistsAsync(UserId, ct).Returns(true);

        await _recorder.RecordAsync(Record(changedBy: UserId), ct);

        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAsync_UserDoesNotExist_ResolvesChangedByToNull()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.UserExistsAsync(UserId, ct).Returns(false);

        await _recorder.RecordAsync(Record(changedBy: UserId), ct);

        _repo.Received(1).Add(Arg.Is<ProcedureInstanceStatusHistory>(h => h.ChangedBy == null));
        await _repo.Received(1).AddEventAsync(
            Arg.Is<ProcedureInstanceEvent>(e => e.CreatedBy == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAsync_NullUser_SkipsFkGuardAndWritesNull()
    {
        var ct = TestContext.Current.CancellationToken;

        await _recorder.RecordAsync(Record(changedBy: null), ct);

        await _repo.DidNotReceive().UserExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _repo.Received(1).Add(Arg.Is<ProcedureInstanceStatusHistory>(h => h.ChangedBy == null));
    }

    [Fact]
    public async Task RecordAsync_NullReason_PersistsNullReason()
    {
        var ct = TestContext.Current.CancellationToken;

        await _recorder.RecordAsync(Record(changedBy: null, reason: null), ct);

        _repo.Received(1).Add(Arg.Is<ProcedureInstanceStatusHistory>(h => h.Reason == null));
    }

    // ── HU #10871 — checklist HÍBRIDO de subsanación en metadata (jsonb genérico, existía sin usar) ──

    [Fact]
    public async Task RecordAsync_NullMetadata_PersistsEmptyJsonObject()
    {
        var ct = TestContext.Current.CancellationToken;

        await _recorder.RecordAsync(
            new TramiteTransitionRecord(
                TenantId, InstanceId, TramiteEstado.Entregado, TramiteEstado.Subsanacion,
                "motivo x", null, ChangedAt, Metadata: null),
            ct);

        _repo.Received(1).Add(Arg.Is<ProcedureInstanceStatusHistory>(h => h.Metadata == "{}"));
    }

    [Fact]
    public async Task RecordAsync_WithMetadata_PersistsItVerbatim()
    {
        var ct = TestContext.Current.CancellationToken;
        const string metadata = "{\"motivo\":\"x\",\"items\":[{\"campo\":\"factura\",\"detalle\":\"falta\"}]}";

        await _recorder.RecordAsync(
            new TramiteTransitionRecord(
                TenantId, InstanceId, TramiteEstado.Entregado, TramiteEstado.Subsanacion,
                "motivo x", null, ChangedAt, Metadata: metadata),
            ct);

        _repo.Received(1).Add(Arg.Is<ProcedureInstanceStatusHistory>(h => h.Metadata == metadata));
    }
}
