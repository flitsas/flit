using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.ResetPassword;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

public sealed class ResetPasswordHandlerTests
{
    private readonly IPasswordResetTokenRepository _tokenRepository = Substitute.For<IPasswordResetTokenRepository>();
    private readonly IUserAccountRepository _userAccountRepository = Substitute.For<IUserAccountRepository>();
    private readonly ISecureTokenGenerator _tokenGenerator = Substitute.For<ISecureTokenGenerator>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly PasswordRecoveryOptions _options = new();
    private readonly IAdminAuditWriter _auditWriter = Substitute.For<IAdminAuditWriter>();
    private readonly IAuditContextAccessor _auditContext = NullAuditContextAccessor.Instance;
    private readonly ResetPasswordHandler _handler;

    public ResetPasswordHandlerTests()
    {
        _handler = new ResetPasswordHandler(
            _tokenRepository, _userAccountRepository, _tokenGenerator, _passwordHasher, _options,
            _auditWriter, _auditContext);
    }

    [Fact]
    public async Task HandleAsync_ValidToken_UpdatesPasswordMarksUsedAndInvalidatesOthers()
    {
        var userId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        _tokenGenerator.HashToken("raw-token").Returns("hash-token");
        _tokenRepository.FindActiveByTokenHashAsync(
                "hash-token", "password_reset", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new PasswordResetTokenRecord(tokenId, userId));
        _passwordHasher.Hash("NewPass123!").Returns("argon2id|salt|hash");

        await _handler.HandleAsync(new ResetPasswordCommand("raw-token", "NewPass123!"), CancellationToken.None);

        await _userAccountRepository.Received(1).UpdatePasswordHashAsync(
            userId, "argon2id|salt|hash", Arg.Any<DateTimeOffset>(), false, Arg.Any<CancellationToken>());
        await _tokenRepository.Received(1).MarkUsedAsync(tokenId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _tokenRepository.Received(1).InvalidateActiveForUserAsync(
            userId, "password_reset", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_InvalidOrExpiredToken_ThrowsInvalidResetToken()
    {
        _tokenGenerator.HashToken(Arg.Any<string>()).Returns("hash-token");
        _tokenRepository.FindActiveByTokenHashAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((PasswordResetTokenRecord?)null);

        var act = () => _handler.HandleAsync(new ResetPasswordCommand("bad-token", "NewPass123!"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidResetTokenException>();
        await _userAccountRepository.DidNotReceiveWithAnyArgs().UpdatePasswordHashAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BlankToken_ThrowsInvalidResetToken()
    {
        var act = () => _handler.HandleAsync(new ResetPasswordCommand("  ", "NewPass123!"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidResetTokenException>();
    }

    [Fact]
    public async Task HandleAsync_TooShortPassword_ThrowsWeakPassword()
    {
        var act = () => _handler.HandleAsync(new ResetPasswordCommand("raw-token", "short"), CancellationToken.None);

        await act.Should().ThrowAsync<WeakPasswordException>();
        await _tokenRepository.DidNotReceiveWithAnyArgs().FindActiveByTokenHashAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
