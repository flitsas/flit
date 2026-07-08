using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

public sealed class CreateInvitationHandlerTests
{
    private readonly IInvitationRepository _repo = Substitute.For<IInvitationRepository>();
    private readonly IUserManagementRepository _userManagementRepo = Substitute.For<IUserManagementRepository>();
    private readonly ISecureTokenGenerator _tokenGen = Substitute.For<ISecureTokenGenerator>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly ILogger<CreateInvitationHandler> _logger = Substitute.For<ILogger<CreateInvitationHandler>>();
    private readonly InvitationOptions _options = new() { ActivateUrlBase = "http://localhost:3000/invite/activate" };
    private readonly CreateInvitationHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid SecondRoleId = Guid.NewGuid();
    private static readonly Guid InvitedBy = Guid.NewGuid();
    private static readonly Guid InvitationId = Guid.NewGuid();
    private const string Email = "nuevo@flit.local";
    private const string FullName = "Nuevo Usuario";

    public CreateInvitationHandlerTests()
    {
        _handler = new CreateInvitationHandler(_repo, _userManagementRepo, _tokenGen, _email, _options, _logger);
        _tokenGen.Generate().Returns(new GeneratedToken("raw-token-abc", "hash-abc"));
        _repo.CreateAsync(Arg.Any<UserInvitationData>(), Arg.Any<CancellationToken>())
            .Returns(InvitationId);
        _repo.RoleExistsInTenantAsync(TenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        // Por defecto el correo no pertenece a ninguna cuenta (activa ni eliminada); los tests de
        // HU #10623 AC4 sobreescriben este stub explícitamente.
        _userManagementRepo.FindByEmailIncludingDeletedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ExistingUserByEmail?)null);
    }

    // AC1 — email no registrado, un rol válido → crea invitación y envía email
    [Fact]
    public async Task HandleAsync_ValidEmailAndRole_CreatesInvitationAndSendsEmail()
    {
        _repo.ExistsPendingAsync(TenantId, Email, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(
            new CreateInvitationCommand(TenantId, Email, FullName, [RoleId], InvitedBy),
            CancellationToken.None);

        result.InvitationId.Should().Be(InvitationId);
        result.Email.Should().Be(Email);
        result.EmailSent.Should().BeTrue();
        await _repo.Received(1).CreateAsync(
            Arg.Is<UserInvitationData>(d =>
                d.TenantId == TenantId &&
                d.Email == Email &&
                d.FullName == FullName &&
                d.RoleIds.Count == 1 && d.RoleIds[0] == RoleId &&
                d.TokenHash == "hash-abc" &&
                d.InvitedBy == InvitedBy),
            Arg.Any<CancellationToken>());
        await _email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.ToEmail == Email && m.HtmlBody.Contains("raw-token-abc")),
            Arg.Any<CancellationToken>());
    }

    // AC4 — invitar con varios roles simultáneos → se validan y crean TODOS
    [Fact]
    public async Task HandleAsync_MultipleRoles_ValidatesAllAndCreatesInvitationWithAllRoleIds()
    {
        _repo.ExistsPendingAsync(TenantId, Email, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(
            new CreateInvitationCommand(TenantId, Email, FullName, [RoleId, SecondRoleId], InvitedBy),
            CancellationToken.None);

        result.InvitationId.Should().Be(InvitationId);
        await _repo.Received(1).RoleExistsInTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>());
        await _repo.Received(1).RoleExistsInTenantAsync(TenantId, SecondRoleId, Arg.Any<CancellationToken>());
        await _repo.Received(1).CreateAsync(
            Arg.Is<UserInvitationData>(d =>
                d.RoleIds.Count == 2 && d.RoleIds.Contains(RoleId) && d.RoleIds.Contains(SecondRoleId)),
            Arg.Any<CancellationToken>());
    }

    // AC5 — invitación sin ningún rol seleccionado → NoRolesSelectedException, sin crear nada
    [Fact]
    public async Task HandleAsync_NoRolesSelected_ThrowsNoRolesSelected()
    {
        await _handler
            .Invoking(h => h.HandleAsync(
                new CreateInvitationCommand(TenantId, Email, FullName, [], InvitedBy),
                CancellationToken.None))
            .Should().ThrowAsync<NoRolesSelectedException>();

        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(
            Arg.Any<UserInvitationData>(), Arg.Any<CancellationToken>());
        await _email.DidNotReceiveWithAnyArgs().SendAsync(
            Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // AC2 HU #10176 — fallo proveedor email → invitación creada pending, EmailSent=false, sin excepción
    [Fact]
    public async Task HandleAsync_EmailSenderThrows_InvitationRemainingPendingAndEmailSentFalse()
    {
        _repo.ExistsPendingAsync(TenantId, Email, Arg.Any<CancellationToken>()).Returns(false);
        _email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP host unreachable"));

        var result = await _handler.HandleAsync(
            new CreateInvitationCommand(TenantId, Email, FullName, [RoleId], InvitedBy),
            CancellationToken.None);

        result.InvitationId.Should().Be(InvitationId);
        result.EmailSent.Should().BeFalse();
        await _repo.Received(1).CreateAsync(Arg.Any<UserInvitationData>(), Arg.Any<CancellationToken>());
    }

    // AC2 — invitación pending para mismo email+tenant → 409 INVITATION_ALREADY_PENDING
    [Fact]
    public async Task HandleAsync_PendingExists_ThrowsInvitationAlreadyPending()
    {
        _repo.ExistsPendingAsync(TenantId, Email, Arg.Any<CancellationToken>()).Returns(true);

        await _handler
            .Invoking(h => h.HandleAsync(
                new CreateInvitationCommand(TenantId, Email, FullName, [RoleId], InvitedBy),
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
        _repo.ExistsPendingAsync(TenantId, Email, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(
            new CreateInvitationCommand(TenantId, Email, FullName, [RoleId], InvitedBy),
            CancellationToken.None);

        result.InvitationId.Should().Be(InvitationId);
        await _repo.Received(1).CreateAsync(Arg.Any<UserInvitationData>(), Arg.Any<CancellationToken>());
    }

    // HU #10623 AC4 — el correo pertenece a una cuenta soft-deleted → mensaje claro, no un error
    // crudo de constraint de BD; no se crea la invitación.
    [Fact]
    public async Task HandleAsync_EmailBelongsToDeletedAccount_ThrowsUserEmailBelongsToDeletedAccount()
    {
        _userManagementRepo.FindByEmailIncludingDeletedAsync(Email, Arg.Any<CancellationToken>())
            .Returns(new ExistingUserByEmail(Guid.NewGuid(), IsDeleted: true));

        await _handler
            .Invoking(h => h.HandleAsync(
                new CreateInvitationCommand(TenantId, Email, FullName, [RoleId], InvitedBy),
                CancellationToken.None))
            .Should().ThrowAsync<UserEmailBelongsToDeletedAccountException>();

        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(
            Arg.Any<UserInvitationData>(), Arg.Any<CancellationToken>());
        await _email.DidNotReceiveWithAnyArgs().SendAsync(
            Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // Rol no pertenece al tenant → RoleNotFoundException
    [Fact]
    public async Task HandleAsync_RoleNotInTenant_ThrowsRoleNotFound()
    {
        _repo.RoleExistsInTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(false);

        await _handler
            .Invoking(h => h.HandleAsync(
                new CreateInvitationCommand(TenantId, Email, FullName, [RoleId], InvitedBy),
                CancellationToken.None))
            .Should().ThrowAsync<RoleNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(
            Arg.Any<UserInvitationData>(), Arg.Any<CancellationToken>());
    }
}
