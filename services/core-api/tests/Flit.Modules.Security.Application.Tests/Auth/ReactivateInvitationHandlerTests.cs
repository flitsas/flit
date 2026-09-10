using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Application.Auth.ReactivateInvitation;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

/// <summary>
/// HU #11552 / ADR-0048 — reactivar una invitación cancelada. Es UNA sola acción: vuelve a
/// "pending", regenera el token (SIEMPRE nuevo) y reenvía el correo. No es idempotente a
/// propósito (segunda llamada → <see cref="InvitationNotCancelledException"/>).
///
/// Uso de ejemplo:
/// var result = await handler.HandleAsync(
///     new ReactivateInvitationCommand(invitationId, scopeTenantId, reactivatedBy),
///     cancellationToken);
/// </summary>
public sealed class ReactivateInvitationHandlerTests
{
    private readonly IInvitationRepository _repo = Substitute.For<IInvitationRepository>();
    private readonly IUserManagementRepository _userManagementRepo = Substitute.For<IUserManagementRepository>();
    private readonly ISecureTokenGenerator _tokenGen = Substitute.For<ISecureTokenGenerator>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly ILogger<ReactivateInvitationHandler> _logger = Substitute.For<ILogger<ReactivateInvitationHandler>>();
    private readonly InvitationOptions _options = new()
    {
        ActivateUrlBase = "http://localhost:3000/invite/activate",
        ResendCooldown = TimeSpan.FromMinutes(2),
    };
    private readonly ReactivateInvitationHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid InvitationId = Guid.NewGuid();
    private static readonly Guid ReactivatedBy = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private const string Email = "cancelada@flit.local";
    private const string FullName = "Usuario Cancelado";

    public ReactivateInvitationHandlerTests()
    {
        _handler = new ReactivateInvitationHandler(_repo, _userManagementRepo, _tokenGen, _email, _options, _logger);
        _tokenGen.Generate().Returns(new GeneratedToken("raw-token-reactivated", "hash-reactivated"));
        _email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        // Camino feliz por defecto: correo libre y rol vigente.
        _userManagementRepo.FindByEmailIncludingDeletedAsync(Email, Arg.Any<CancellationToken>())
            .Returns((ExistingUserByEmail?)null);
        _repo.ExistsPendingAsync(Arg.Any<Guid>(), Email, Arg.Any<CancellationToken>()).Returns(false);
        _repo.UserExistsWithEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(false);
        _repo.RoleExistsInTenantAsync(Arg.Any<Guid>(), RoleId, Arg.Any<CancellationToken>()).Returns(true);
    }

    private static InvitationForReactivate Cancelled(
        Guid tenantId, DateTimeOffset? lastSentAt = null, IReadOnlyList<Guid>? roleIds = null) =>
        new(InvitationId, tenantId, Email, FullName, "cancelled", lastSentAt, roleIds ?? [RoleId]);

    // Happy path — invitación cancelada de mi alcance: vuelve a pending, token nuevo, correo reenviado.
    [Fact]
    public async Task HandleAsync_CancelledInvitationInScope_ReactivatesAndSendsEmail()
    {
        _repo.FindForReactivateAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Cancelled(TenantId));

        var result = await _handler.HandleAsync(
            new ReactivateInvitationCommand(InvitationId, TenantId, ReactivatedBy),
            CancellationToken.None);

        result.InvitationId.Should().Be(InvitationId);
        result.Email.Should().Be(Email);
        result.EmailSent.Should().BeTrue();

        await _repo.Received(1).ReactivateAsync(
            InvitationId, "hash-reactivated", Arg.Any<DateTimeOffset>(), ReactivatedBy, Arg.Any<CancellationToken>());
        await _email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.ToEmail == Email && m.HtmlBody.Contains("raw-token-reactivated")),
            Arg.Any<CancellationToken>());
    }

    // Token viejo muerto — el token regenerado es SIEMPRE nuevo, nunca reutiliza el hash anterior.
    [Fact]
    public async Task HandleAsync_AlwaysRegeneratesToken_NeverReusesOldHash()
    {
        _repo.FindForReactivateAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Cancelled(TenantId));

        await _handler.HandleAsync(
            new ReactivateInvitationCommand(InvitationId, TenantId, ReactivatedBy),
            CancellationToken.None);

        _tokenGen.Received(1).Generate();
        await _repo.DidNotReceive().ReactivateAsync(
            InvitationId, Arg.Is<string>(h => h != "hash-reactivated"), Arg.Any<DateTimeOffset>(),
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // No es idempotente — invitación "pending" (segunda reactivación) → 409, sin tocar nada.
    [Fact]
    public async Task HandleAsync_InvitationAlreadyPending_ThrowsInvitationNotCancelled()
    {
        var pending = new InvitationForReactivate(InvitationId, TenantId, Email, FullName, "pending", null, [RoleId]);
        _repo.FindForReactivateAsync(InvitationId, TenantId, Arg.Any<CancellationToken>()).Returns(pending);

        await _handler
            .Invoking(h => h.HandleAsync(
                new ReactivateInvitationCommand(InvitationId, TenantId, ReactivatedBy),
                CancellationToken.None))
            .Should().ThrowAsync<InvitationNotCancelledException>();

        await _repo.DidNotReceiveWithAnyArgs().ReactivateAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _email.DidNotReceiveWithAnyArgs().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // Invitación ya aceptada → mismo error que "pending": solo "cancelled" es reactivable.
    [Fact]
    public async Task HandleAsync_InvitationAccepted_ThrowsInvitationNotCancelled()
    {
        var accepted = new InvitationForReactivate(InvitationId, TenantId, Email, FullName, "accepted", null, [RoleId]);
        _repo.FindForReactivateAsync(InvitationId, TenantId, Arg.Any<CancellationToken>()).Returns(accepted);

        await _handler
            .Invoking(h => h.HandleAsync(
                new ReactivateInvitationCommand(InvitationId, TenantId, ReactivatedBy),
                CancellationToken.None))
            .Should().ThrowAsync<InvitationNotCancelledException>();
    }

    // Invitación inexistente o fuera de alcance (tenant distinto) → not found, sin distinguir.
    [Fact]
    public async Task HandleAsync_InvitationNotFoundInScope_ThrowsInvitationNotFound()
    {
        _repo.FindForReactivateAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns((InvitationForReactivate?)null);

        await _handler
            .Invoking(h => h.HandleAsync(
                new ReactivateInvitationCommand(InvitationId, TenantId, ReactivatedBy),
                CancellationToken.None))
            .Should().ThrowAsync<InvitationNotFoundException>();

        await _email.DidNotReceiveWithAnyArgs().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // SuperAdmin (ScopeTenantId null) → el repo se consulta sin restricción de tenant.
    [Fact]
    public async Task HandleAsync_SuperAdminScope_QueriesRepositoryWithNullTenant()
    {
        _repo.FindForReactivateAsync(InvitationId, null, Arg.Any<CancellationToken>())
            .Returns(Cancelled(OtherTenantId));

        var result = await _handler.HandleAsync(
            new ReactivateInvitationCommand(InvitationId, null, ReactivatedBy),
            CancellationToken.None);

        result.InvitationId.Should().Be(InvitationId);
        await _repo.Received(1).FindForReactivateAsync(InvitationId, null, Arg.Any<CancellationToken>());
    }

    // Colisión con otra invitación pendiente del mismo correo en el tenant → 409, pre-validado
    // antes de tocar la fila cancelada.
    [Fact]
    public async Task HandleAsync_AnotherPendingInvitationForSameEmail_ThrowsInvitationAlreadyPending()
    {
        _repo.FindForReactivateAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Cancelled(TenantId));
        _repo.ExistsPendingAsync(TenantId, Email, Arg.Any<CancellationToken>()).Returns(true);

        await _handler
            .Invoking(h => h.HandleAsync(
                new ReactivateInvitationCommand(InvitationId, TenantId, ReactivatedBy),
                CancellationToken.None))
            .Should().ThrowAsync<InvitationAlreadyPendingException>();

        await _repo.DidNotReceiveWithAnyArgs().ReactivateAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // Ya existe un usuario activo con ese correo → 409, no se puede reactivar.
    [Fact]
    public async Task HandleAsync_UserAlreadyExistsWithEmail_ThrowsUserAlreadyExists()
    {
        _repo.FindForReactivateAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Cancelled(TenantId));
        _repo.UserExistsWithEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(true);

        await _handler
            .Invoking(h => h.HandleAsync(
                new ReactivateInvitationCommand(InvitationId, TenantId, ReactivatedBy),
                CancellationToken.None))
            .Should().ThrowAsync<UserAlreadyExistsException>();
    }

    // Correo pertenece a una cuenta soft-deleted → 409, mismo criterio que CreateInvitationHandler.
    [Fact]
    public async Task HandleAsync_EmailBelongsToDeletedAccount_ThrowsUserEmailBelongsToDeletedAccount()
    {
        _repo.FindForReactivateAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Cancelled(TenantId));
        _userManagementRepo.FindByEmailIncludingDeletedAsync(Email, Arg.Any<CancellationToken>())
            .Returns(new ExistingUserByEmail(Guid.NewGuid(), true));

        await _handler
            .Invoking(h => h.HandleAsync(
                new ReactivateInvitationCommand(InvitationId, TenantId, ReactivatedBy),
                CancellationToken.None))
            .Should().ThrowAsync<UserEmailBelongsToDeletedAccountException>();

        await _repo.DidNotReceiveWithAnyArgs().ReactivateAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // Alguno de los roles de la invitación ya no está activo → 409, no revive con un rol muerto.
    [Fact]
    public async Task HandleAsync_RoleNoLongerActive_ThrowsRoleNotFound()
    {
        _repo.FindForReactivateAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Cancelled(TenantId));
        _repo.RoleExistsInTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(false);

        await _handler
            .Invoking(h => h.HandleAsync(
                new ReactivateInvitationCommand(InvitationId, TenantId, ReactivatedBy),
                CancellationToken.None))
            .Should().ThrowAsync<RoleNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().ReactivateAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // Cooldown antiabuso compartido con /resend — reactivada/reenviada hace menos del cooldown.
    [Fact]
    public async Task HandleAsync_WithinCooldown_ThrowsResendCooldownActive()
    {
        _repo.FindForReactivateAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Cancelled(TenantId, lastSentAt: DateTimeOffset.UtcNow.AddSeconds(-30)));

        var exception = await _handler
            .Invoking(h => h.HandleAsync(
                new ReactivateInvitationCommand(InvitationId, TenantId, ReactivatedBy),
                CancellationToken.None))
            .Should().ThrowAsync<ResendCooldownActiveException>();

        exception.Which.RetryAfter.Should().BePositive();
        exception.Which.RetryAfter.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(2));

        await _repo.DidNotReceiveWithAnyArgs().ReactivateAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // Reactivada hace más del cooldown configurado → puede reactivarse sin problema.
    [Fact]
    public async Task HandleAsync_LastSentLongAgo_AllowsReactivate()
    {
        _repo.FindForReactivateAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Cancelled(TenantId, lastSentAt: DateTimeOffset.UtcNow.AddMinutes(-10)));

        var result = await _handler.HandleAsync(
            new ReactivateInvitationCommand(InvitationId, TenantId, ReactivatedBy),
            CancellationToken.None);

        result.InvitationId.Should().Be(InvitationId);
        await _repo.Received(1).ReactivateAsync(
            InvitationId, "hash-reactivated", Arg.Any<DateTimeOffset>(), ReactivatedBy, Arg.Any<CancellationToken>());
    }

    // HU #11358-equivalente — fallo tipado del proveedor de email: la invitación queda reactivada
    // igual (EmailSent=false no revierte la transición, mismo criterio que CreateInvitationHandler).
    [Fact]
    public async Task HandleAsync_EmailSenderFails_ReactivatedButEmailSentFalse()
    {
        _repo.FindForReactivateAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Cancelled(TenantId));
        _email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable)));

        var result = await _handler.HandleAsync(
            new ReactivateInvitationCommand(InvitationId, TenantId, ReactivatedBy),
            CancellationToken.None);

        result.EmailSent.Should().BeFalse();
        await _repo.Received(1).ReactivateAsync(
            InvitationId, "hash-reactivated", Arg.Any<DateTimeOffset>(), ReactivatedBy, Arg.Any<CancellationToken>());
    }
}
