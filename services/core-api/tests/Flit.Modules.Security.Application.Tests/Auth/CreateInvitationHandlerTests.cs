using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

public sealed class CreateInvitationHandlerTests
{
    private readonly IInvitationRepository _repo = Substitute.For<IInvitationRepository>();
    private readonly ISecureTokenGenerator _tokenGen = Substitute.For<ISecureTokenGenerator>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly ILogger<CreateInvitationHandler> _logger = Substitute.For<ILogger<CreateInvitationHandler>>();
    private readonly InvitationOptions _options = new() { ActivateUrlBase = "http://localhost:3000/invite/activate" };
    private readonly CreateInvitationHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid InvitedBy = Guid.NewGuid();
    private static readonly Guid InvitationId = Guid.NewGuid();
    private const string Email = "nuevo@flit.local";
    private const string FullName = "Nuevo Usuario";

    public CreateInvitationHandlerTests()
    {
        _handler = new CreateInvitationHandler(_repo, _tokenGen, _email, _options, _logger);
        _tokenGen.Generate().Returns(new GeneratedToken("raw-token-abc", "hash-abc"));
        _repo.CreateAsync(Arg.Any<UserInvitationData>(), Arg.Any<CancellationToken>())
            .Returns(InvitationId);
    }

    // AC1 — email no registrado, rol válido → crea invitación y envía email
    [Fact]
    public async Task HandleAsync_ValidEmailAndRole_CreatesInvitationAndSendsEmail()
    {
        _repo.RoleExistsInTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.ExistsPendingAsync(TenantId, Email, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(
            new CreateInvitationCommand(TenantId, Email, FullName, RoleId, InvitedBy),
            CancellationToken.None);

        result.InvitationId.Should().Be(InvitationId);
        result.Email.Should().Be(Email);
        result.EmailSent.Should().BeTrue();
        await _repo.Received(1).CreateAsync(
            Arg.Is<UserInvitationData>(d =>
                d.TenantId == TenantId &&
                d.Email == Email &&
                d.FullName == FullName &&
                d.RoleId == RoleId &&
                d.TokenHash == "hash-abc" &&
                d.InvitedBy == InvitedBy),
            Arg.Any<CancellationToken>());
        await _email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.ToEmail == Email && m.HtmlBody.Contains("raw-token-abc")),
            Arg.Any<CancellationToken>());
    }

    // AC: rol nulo → se omite validación de rol, se crea invitación sin rol
    [Fact]
    public async Task HandleAsync_NullRole_SkipsRoleValidationAndCreatesInvitation()
    {
        _repo.ExistsPendingAsync(TenantId, Email, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(
            new CreateInvitationCommand(TenantId, Email, FullName, null, InvitedBy),
            CancellationToken.None);

        result.InvitationId.Should().Be(InvitationId);
        result.EmailSent.Should().BeTrue();
        await _repo.DidNotReceiveWithAnyArgs().RoleExistsInTenantAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).CreateAsync(
            Arg.Is<UserInvitationData>(d => d.RoleId == null),
            Arg.Any<CancellationToken>());
    }

    // AC2 HU #10176 — fallo proveedor email → invitación creada pending, EmailSent=false, sin excepción
    [Fact]
    public async Task HandleAsync_EmailSenderThrows_InvitationRemainingPendingAndEmailSentFalse()
    {
        _repo.RoleExistsInTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.ExistsPendingAsync(TenantId, Email, Arg.Any<CancellationToken>()).Returns(false);
        _email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP host unreachable"));

        var result = await _handler.HandleAsync(
            new CreateInvitationCommand(TenantId, Email, FullName, RoleId, InvitedBy),
            CancellationToken.None);

        result.InvitationId.Should().Be(InvitationId);
        result.EmailSent.Should().BeFalse();
        await _repo.Received(1).CreateAsync(Arg.Any<UserInvitationData>(), Arg.Any<CancellationToken>());
    }

    // AC2 — invitación pending para mismo email+tenant → 409 INVITATION_ALREADY_PENDING
    [Fact]
    public async Task HandleAsync_PendingExists_ThrowsInvitationAlreadyPending()
    {
        _repo.RoleExistsInTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.ExistsPendingAsync(TenantId, Email, Arg.Any<CancellationToken>()).Returns(true);

        await _handler
            .Invoking(h => h.HandleAsync(
                new CreateInvitationCommand(TenantId, Email, FullName, RoleId, InvitedBy),
                CancellationToken.None))
            .Should().ThrowAsync<InvitationAlreadyPendingException>();

        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(
            Arg.Any<UserInvitationData>(), Arg.Any<CancellationToken>());
        await _email.DidNotReceiveWithAnyArgs().SendAsync(
            Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // AC3 — invitación accepted previa no bloquea nueva pending (índice parcial)
    [Fact]
    public async Task HandleAsync_PreviousAcceptedInvitation_AllowsNewPending()
    {
        // El repo devuelve false porque la invitación accepted no cuenta como pending
        _repo.RoleExistsInTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.ExistsPendingAsync(TenantId, Email, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(
            new CreateInvitationCommand(TenantId, Email, FullName, RoleId, InvitedBy),
            CancellationToken.None);

        result.InvitationId.Should().Be(InvitationId);
        await _repo.Received(1).CreateAsync(Arg.Any<UserInvitationData>(), Arg.Any<CancellationToken>());
    }

    // Rol no pertenece al tenant → RoleNotFoundException
    [Fact]
    public async Task HandleAsync_RoleNotInTenant_ThrowsRoleNotFound()
    {
        _repo.RoleExistsInTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(false);

        await _handler
            .Invoking(h => h.HandleAsync(
                new CreateInvitationCommand(TenantId, Email, FullName, RoleId, InvitedBy),
                CancellationToken.None))
            .Should().ThrowAsync<RoleNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(
            Arg.Any<UserInvitationData>(), Arg.Any<CancellationToken>());
    }
}
