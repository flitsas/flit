using Flit.Modules.Security.Application.Auth.CancelInvitation;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

/// <summary>
/// HU #10627 — Cancelar invitación pendiente. Distinta de reenviar (HU #10625): anula la
/// invitación en vez de reenviar el correo.
/// </summary>
public sealed class CancelInvitationHandlerTests
{
    private readonly IInvitationRepository _repo = Substitute.For<IInvitationRepository>();
    private readonly CancelInvitationHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid InvitationId = Guid.NewGuid();
    private static readonly Guid CancelledBy = Guid.NewGuid();

    public CancelInvitationHandlerTests()
    {
        _handler = new CancelInvitationHandler(_repo);
    }

    // AC1 — invitación pendiente de mi alcance → se cancela (Status="cancelled" + soft-delete)
    [Fact]
    public async Task HandleAsync_PendingInvitationInScope_CancelsIt()
    {
        _repo.FindByIdAsync(InvitationId, Arg.Any<CancellationToken>())
            .Returns(new InvitationStatusInfo(InvitationId, TenantId, "pending"));

        await _handler.HandleAsync(
            new CancelInvitationCommand(InvitationId, TenantId, CancelledBy),
            CancellationToken.None);

        await _repo.Received(1).CancelAsync(InvitationId, CancelledBy, Arg.Any<CancellationToken>());
    }

    // AC1 (alcance SuperAdmin) — ScopeTenantId nulo (SuperAdmin) cancela sin restricción de tenant
    [Fact]
    public async Task HandleAsync_NullScopeTenantId_CancelsRegardlessOfTenant()
    {
        _repo.FindByIdAsync(InvitationId, Arg.Any<CancellationToken>())
            .Returns(new InvitationStatusInfo(InvitationId, OtherTenantId, "pending"));

        await _handler.HandleAsync(
            new CancelInvitationCommand(InvitationId, null, CancelledBy),
            CancellationToken.None);

        await _repo.Received(1).CancelAsync(InvitationId, CancelledBy, Arg.Any<CancellationToken>());
    }

    // AC2 — invitación ya aceptada → InvitationNotPendingException, sin cancelar nada
    [Fact]
    public async Task HandleAsync_AlreadyAcceptedInvitation_ThrowsInvitationNotPending()
    {
        _repo.FindByIdAsync(InvitationId, Arg.Any<CancellationToken>())
            .Returns(new InvitationStatusInfo(InvitationId, TenantId, "accepted"));

        await _handler
            .Invoking(h => h.HandleAsync(
                new CancelInvitationCommand(InvitationId, TenantId, CancelledBy),
                CancellationToken.None))
            .Should().ThrowAsync<InvitationNotPendingException>();

        await _repo.DidNotReceiveWithAnyArgs().CancelAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // AC2 — invitación ya cancelada previamente → InvitationNotPendingException (no-op silencioso prohibido)
    [Fact]
    public async Task HandleAsync_AlreadyCancelledInvitation_ThrowsInvitationNotPending()
    {
        _repo.FindByIdAsync(InvitationId, Arg.Any<CancellationToken>())
            .Returns(new InvitationStatusInfo(InvitationId, TenantId, "cancelled"));

        await _handler
            .Invoking(h => h.HandleAsync(
                new CancelInvitationCommand(InvitationId, TenantId, CancelledBy),
                CancellationToken.None))
            .Should().ThrowAsync<InvitationNotPendingException>();

        await _repo.DidNotReceiveWithAnyArgs().CancelAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // Invitación inexistente → InvitationNotFoundException
    [Fact]
    public async Task HandleAsync_InvitationNotFound_ThrowsInvitationNotFound()
    {
        _repo.FindByIdAsync(InvitationId, Arg.Any<CancellationToken>())
            .Returns((InvitationStatusInfo?)null);

        await _handler
            .Invoking(h => h.HandleAsync(
                new CancelInvitationCommand(InvitationId, TenantId, CancelledBy),
                CancellationToken.None))
            .Should().ThrowAsync<InvitationNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().CancelAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // Invitación existe pero pertenece a otro tenant (fuera de alcance) → InvitationNotFoundException
    [Fact]
    public async Task HandleAsync_InvitationOutOfScope_ThrowsInvitationNotFound()
    {
        _repo.FindByIdAsync(InvitationId, Arg.Any<CancellationToken>())
            .Returns(new InvitationStatusInfo(InvitationId, OtherTenantId, "pending"));

        await _handler
            .Invoking(h => h.HandleAsync(
                new CancelInvitationCommand(InvitationId, TenantId, CancelledBy),
                CancellationToken.None))
            .Should().ThrowAsync<InvitationNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().CancelAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
