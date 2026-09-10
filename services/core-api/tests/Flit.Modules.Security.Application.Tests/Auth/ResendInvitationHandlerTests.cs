using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Application.Auth.ResendInvitation;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

public sealed class ResendInvitationHandlerTests
{
    private readonly IInvitationRepository _repo = Substitute.For<IInvitationRepository>();
    private readonly ISecureTokenGenerator _tokenGen = Substitute.For<ISecureTokenGenerator>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly ILogger<ResendInvitationHandler> _logger = Substitute.For<ILogger<ResendInvitationHandler>>();
    private readonly InvitationOptions _options = new()
    {
        ActivateUrlBase = "http://localhost:3000/invite/activate",
        ResendCooldown = TimeSpan.FromMinutes(2),
    };
    private readonly ResendInvitationHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid InvitationId = Guid.NewGuid();
    private static readonly Guid ResentBy = Guid.NewGuid();
    private const string Email = "pendiente@flit.local";
    private const string FullName = "Usuario Pendiente";

    public ResendInvitationHandlerTests()
    {
        _handler = new ResendInvitationHandler(_repo, _tokenGen, _email, _options, _logger);
        _tokenGen.Generate().Returns(new GeneratedToken("raw-token-new", "hash-new"));
        // HU #11358 — por defecto el sender simula éxito (antes lo hacía implícitamente un Task
        // no configurado).
        _email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
    }

    // AC1 — invitación pendiente sin envío previo → regenera token, reenvía correo
    [Fact]
    public async Task HandleAsync_PendingInvitationNeverSent_RegeneratesTokenAndSendsEmail()
    {
        _repo.FindForResendAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns(new InvitationForResend(InvitationId, TenantId, Email, FullName, "pending", null));

        var result = await _handler.HandleAsync(
            new ResendInvitationCommand(InvitationId, TenantId, ResentBy),
            CancellationToken.None);

        result.InvitationId.Should().Be(InvitationId);
        result.Email.Should().Be(Email);
        result.EmailSent.Should().BeTrue();

        await _repo.Received(1).UpdateResendAsync(
            InvitationId, "hash-new", Arg.Any<DateTimeOffset>(), ResentBy, Arg.Any<CancellationToken>());
        await _email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.ToEmail == Email && m.HtmlBody.Contains("raw-token-new")),
            Arg.Any<CancellationToken>());
    }

    // AC1 — invitación pendiente enviada hace más del cooldown → puede reenviarse de nuevo
    [Fact]
    public async Task HandleAsync_PendingInvitationSentLongAgo_AllowsResend()
    {
        _repo.FindForResendAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns(new InvitationForResend(
                InvitationId, TenantId, Email, FullName, "pending", DateTimeOffset.UtcNow.AddMinutes(-10)));

        var result = await _handler.HandleAsync(
            new ResendInvitationCommand(InvitationId, TenantId, ResentBy),
            CancellationToken.None);

        result.InvitationId.Should().Be(InvitationId);
        await _repo.Received(1).UpdateResendAsync(
            InvitationId, "hash-new", Arg.Any<DateTimeOffset>(), ResentBy, Arg.Any<CancellationToken>());
    }

    // AC2 — reenviada hace menos del cooldown configurado → rechazo, sin regenerar token
    [Fact]
    public async Task HandleAsync_WithinCooldown_ThrowsResendCooldownActive()
    {
        _repo.FindForResendAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns(new InvitationForResend(
                InvitationId, TenantId, Email, FullName, "pending", DateTimeOffset.UtcNow.AddSeconds(-30)));

        var exception = await _handler
            .Invoking(h => h.HandleAsync(
                new ResendInvitationCommand(InvitationId, TenantId, ResentBy),
                CancellationToken.None))
            .Should().ThrowAsync<ResendCooldownActiveException>();

        exception.Which.RetryAfter.Should().BePositive();
        exception.Which.RetryAfter.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(2));

        await _repo.DidNotReceiveWithAnyArgs().UpdateResendAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _email.DidNotReceiveWithAnyArgs().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // AC3 — invitación ya aceptada → error claro, sin regenerar token ni reenviar
    [Fact]
    public async Task HandleAsync_InvitationAccepted_ThrowsInvitationNotPending()
    {
        _repo.FindForResendAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns(new InvitationForResend(InvitationId, TenantId, Email, FullName, "accepted", null));

        await _handler
            .Invoking(h => h.HandleAsync(
                new ResendInvitationCommand(InvitationId, TenantId, ResentBy),
                CancellationToken.None))
            .Should().ThrowAsync<InvitationNotPendingException>();

        await _repo.DidNotReceiveWithAnyArgs().UpdateResendAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _email.DidNotReceiveWithAnyArgs().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // AC3 — invitación cancelada → mismo error claro que "accepted"
    [Fact]
    public async Task HandleAsync_InvitationCancelled_ThrowsInvitationNotPending()
    {
        _repo.FindForResendAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns(new InvitationForResend(InvitationId, TenantId, Email, FullName, "cancelled", null));

        await _handler
            .Invoking(h => h.HandleAsync(
                new ResendInvitationCommand(InvitationId, TenantId, ResentBy),
                CancellationToken.None))
            .Should().ThrowAsync<InvitationNotPendingException>();
    }

    // Invitación inexistente o fuera del alcance del caller (tenant distinto) → not found
    [Fact]
    public async Task HandleAsync_InvitationNotFoundInScope_ThrowsInvitationNotFound()
    {
        _repo.FindForResendAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns((InvitationForResend?)null);

        await _handler
            .Invoking(h => h.HandleAsync(
                new ResendInvitationCommand(InvitationId, TenantId, ResentBy),
                CancellationToken.None))
            .Should().ThrowAsync<InvitationNotFoundException>();

        await _email.DidNotReceiveWithAnyArgs().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // SuperAdmin (ScopeTenantId null) → el repo se consulta sin restricción de tenant
    [Fact]
    public async Task HandleAsync_SuperAdminScope_QueriesRepositoryWithNullTenant()
    {
        _repo.FindForResendAsync(InvitationId, null, Arg.Any<CancellationToken>())
            .Returns(new InvitationForResend(InvitationId, TenantId, Email, FullName, "pending", null));

        var result = await _handler.HandleAsync(
            new ResendInvitationCommand(InvitationId, null, ResentBy),
            CancellationToken.None);

        result.InvitationId.Should().Be(InvitationId);
        await _repo.Received(1).FindForResendAsync(InvitationId, null, Arg.Any<CancellationToken>());
    }

    // HU #11358 AC2/AC3 — fallo tipado del proveedor de email (ya no una excepción) → invitación
    // queda con el token ya regenerado, EmailSent=false, sin excepción propagada.
    [Fact]
    public async Task HandleAsync_EmailSenderFails_TokenRegeneratedButEmailSentFalse()
    {
        _repo.FindForResendAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns(new InvitationForResend(InvitationId, TenantId, Email, FullName, "pending", null));
        _email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable)));

        var result = await _handler.HandleAsync(
            new ResendInvitationCommand(InvitationId, TenantId, ResentBy),
            CancellationToken.None);

        result.EmailSent.Should().BeFalse();
        await _repo.Received(1).UpdateResendAsync(
            InvitationId, "hash-new", Arg.Any<DateTimeOffset>(), ResentBy, Arg.Any<CancellationToken>());
    }

    // HU #11358 AC1 — el tenant PROPIETARIO de la invitación (no el ScopeTenantId del caller)
    // viaja explícito en la solicitud de envío.
    [Fact]
    public async Task HandleAsync_SendsMessageWithInvitationTenantId()
    {
        _repo.FindForResendAsync(InvitationId, TenantId, Arg.Any<CancellationToken>())
            .Returns(new InvitationForResend(InvitationId, TenantId, Email, FullName, "pending", null));

        await _handler.HandleAsync(
            new ResendInvitationCommand(InvitationId, TenantId, ResentBy),
            CancellationToken.None);

        await _email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.TenantId == TenantId), Arg.Any<CancellationToken>());
    }
}
