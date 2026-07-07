using Flit.Modules.Security.Application.Auth.ActivateAccount;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

public sealed class ActivateAccountHandlerTests
{
    private readonly IInvitationRepository _invitationRepo = Substitute.For<IInvitationRepository>();
    private readonly ISecureTokenGenerator _tokenGen = Substitute.For<ISecureTokenGenerator>();
    private readonly IUserActivationRepository _activationRepo = Substitute.For<IUserActivationRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ActivateAccountHandler _handler;

    private static readonly Guid InvitationId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid SecondRoleId = Guid.NewGuid();
    private static readonly Guid InvitedBy = Guid.NewGuid();
    private const string RawToken = "raw-token-abc";
    private const string TokenHash = "hash-abc";
    private const string ValidPassword = "FlitPass1!";

    private readonly PendingInvitation _pendingInvitation = new(
        InvitationId, TenantId, "invited@flit.local", "Usuario Invitado", [RoleId], InvitedBy);

    private readonly PendingInvitation _pendingInvitationTwoRoles = new(
        InvitationId, TenantId, "invited@flit.local", "Usuario Multi Rol", [RoleId, SecondRoleId], InvitedBy);

    public ActivateAccountHandlerTests()
    {
        _handler = new ActivateAccountHandler(_invitationRepo, _tokenGen, _activationRepo, _hasher);
        _tokenGen.HashToken(RawToken).Returns(TokenHash);
        _hasher.Hash(ValidPassword).Returns("hashed-password");
    }

    // AC1 — token válido, contraseña correcta, con un rol → usuario creado e invitación aceptada
    [Fact]
    public async Task HandleAsync_ValidTokenAndPassword_ActivatesAccount()
    {
        _invitationRepo.FindPendingByTokenHashAsync(TokenHash, Arg.Any<CancellationToken>())
            .Returns(_pendingInvitation);

        var result = await _handler.HandleAsync(
            new ActivateAccountCommand(RawToken, ValidPassword),
            CancellationToken.None);

        result.Should().NotBeNull();
        await _activationRepo.Received(1).ActivateAsync(
            Arg.Is<ActivationData>(d =>
                d.InvitationId == InvitationId &&
                d.Email == "invited@flit.local" &&
                d.FullName == "Usuario Invitado" &&
                d.TenantId == TenantId &&
                d.RoleIds.Count == 1 && d.RoleIds[0] == RoleId &&
                d.PasswordHash == "hashed-password"),
            Arg.Any<CancellationToken>());
    }

    // AC4 — invitación con varios roles → la activación propaga TODOS los RoleIds
    [Fact]
    public async Task HandleAsync_ValidTokenWithMultipleRoles_ActivatesAccountWithAllRoles()
    {
        _invitationRepo.FindPendingByTokenHashAsync(TokenHash, Arg.Any<CancellationToken>())
            .Returns(_pendingInvitationTwoRoles);

        var result = await _handler.HandleAsync(
            new ActivateAccountCommand(RawToken, ValidPassword),
            CancellationToken.None);

        result.Should().NotBeNull();
        await _activationRepo.Received(1).ActivateAsync(
            Arg.Is<ActivationData>(d =>
                d.InvitationId == InvitationId &&
                d.RoleIds.Count == 2 && d.RoleIds.Contains(RoleId) && d.RoleIds.Contains(SecondRoleId)),
            Arg.Any<CancellationToken>());
    }

    // AC2 — token inexistente o no pending → 400 INVITATION_INVALID
    [Fact]
    public async Task HandleAsync_InvalidToken_ThrowsInvalidInvitationToken()
    {
        _invitationRepo.FindPendingByTokenHashAsync(TokenHash, Arg.Any<CancellationToken>())
            .ReturnsNull();

        await _handler
            .Invoking(h => h.HandleAsync(
                new ActivateAccountCommand(RawToken, ValidPassword),
                CancellationToken.None))
            .Should().ThrowAsync<InvalidInvitationTokenException>();

        await _activationRepo.DidNotReceiveWithAnyArgs().ActivateAsync(
            Arg.Any<ActivationData>(), Arg.Any<CancellationToken>());
    }

    // Contraseña débil con token válido → WeakPasswordException, sin crear usuario
    [Fact]
    public async Task HandleAsync_WeakPassword_ThrowsWeakPasswordException()
    {
        _invitationRepo.FindPendingByTokenHashAsync(TokenHash, Arg.Any<CancellationToken>())
            .Returns(_pendingInvitation);

        await _handler
            .Invoking(h => h.HandleAsync(
                new ActivateAccountCommand(RawToken, "weak"),
                CancellationToken.None))
            .Should().ThrowAsync<WeakPasswordException>();

        await _activationRepo.DidNotReceiveWithAnyArgs().ActivateAsync(
            Arg.Any<ActivationData>(), Arg.Any<CancellationToken>());
    }
}
