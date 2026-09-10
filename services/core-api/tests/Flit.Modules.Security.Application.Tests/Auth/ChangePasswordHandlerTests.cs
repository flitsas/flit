using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.ChangePassword;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

public sealed class ChangePasswordHandlerTests
{
    private readonly IUserAccountRepository _repo = Substitute.For<IUserAccountRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly PasswordRecoveryOptions _options = new();
    private readonly IAdminAuditWriter _auditWriter = Substitute.For<IAdminAuditWriter>();
    private readonly IAuditContextAccessor _auditContext = NullAuditContextAccessor.Instance;
    private readonly ChangePasswordHandler _handler;
    private readonly Guid _userId = Guid.NewGuid();

    public ChangePasswordHandlerTests()
    {
        _handler = new ChangePasswordHandler(_repo, _hasher, _options, _auditWriter, _auditContext);
    }

    [Fact]
    public async Task HandleAsync_ValidCurrentAndCompliantNew_PersistsNewHash()
    {
        _repo.GetPasswordHashAsync(_userId, Arg.Any<CancellationToken>()).Returns("current-hash");
        _hasher.Verify("Current1!", "current-hash").Returns(true);
        _hasher.Hash("NewPass123").Returns("new-hash");

        await _handler.HandleAsync(new ChangePasswordCommand(_userId, "Current1!", "NewPass123"), CancellationToken.None);

        await _repo.Received(1).UpdatePasswordHashAsync(
            _userId, "new-hash", Arg.Any<DateTimeOffset>(), false, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("short1")]      // < 8
    [InlineData("alllowercase123")] // sin mayúscula
    [InlineData("ALLUPPERCASE123")] // sin minúscula
    [InlineData("NoDigitsHere")]    // sin dígito
    public async Task HandleAsync_NonCompliantNewPassword_ThrowsWeakPassword(string weak)
    {
        _repo.GetPasswordHashAsync(_userId, Arg.Any<CancellationToken>()).Returns("current-hash");
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        await _handler.Invoking(h => h.HandleAsync(new ChangePasswordCommand(_userId, "Current1!", weak), CancellationToken.None))
            .Should().ThrowAsync<WeakPasswordException>();
        await _repo.DidNotReceiveWithAnyArgs().UpdatePasswordHashAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WrongCurrentPassword_ThrowsInvalidCurrent()
    {
        _repo.GetPasswordHashAsync(_userId, Arg.Any<CancellationToken>()).Returns("current-hash");
        _hasher.Verify("WrongCurrent", "current-hash").Returns(false);

        await _handler.Invoking(h => h.HandleAsync(new ChangePasswordCommand(_userId, "WrongCurrent", "NewPass123"), CancellationToken.None))
            .Should().ThrowAsync<InvalidCurrentPasswordException>();
    }

    [Fact]
    public async Task HandleAsync_NoCredential_ThrowsInvalidCredentials()
    {
        _repo.GetPasswordHashAsync(_userId, Arg.Any<CancellationToken>()).Returns((string?)null);

        await _handler.Invoking(h => h.HandleAsync(new ChangePasswordCommand(_userId, "Current1!", "NewPass123"), CancellationToken.None))
            .Should().ThrowAsync<InvalidCredentialsException>();
    }

    // HU #11553 AC1 — la nueva contraseña es idéntica a la vigente → PasswordReusedException,
    // sin persistir el hash.
    [Fact]
    public async Task HandleAsync_NewPasswordSameAsCurrent_ThrowsPasswordReused()
    {
        _repo.GetPasswordHashAsync(_userId, Arg.Any<CancellationToken>()).Returns("current-hash");
        _hasher.Verify("Current1!", "current-hash").Returns(true);

        await _handler.Invoking(h => h.HandleAsync(new ChangePasswordCommand(_userId, "Current1!", "Current1!"), CancellationToken.None))
            .Should().ThrowAsync<PasswordReusedException>();

        await _repo.DidNotReceiveWithAnyArgs().UpdatePasswordHashAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    // HU #11553 AC1 — la política de complejidad se evalúa ANTES que la reutilización: una
    // contraseña débil (aunque coincida con la actual) debe fallar por WeakPassword, no reuso.
    [Fact]
    public async Task HandleAsync_WeakPasswordThatAlsoMatchesCurrent_ThrowsWeakPasswordNotReused()
    {
        _repo.GetPasswordHashAsync(_userId, Arg.Any<CancellationToken>()).Returns("current-hash");
        _hasher.Verify(Arg.Any<string>(), "current-hash").Returns(true);

        await _handler.Invoking(h => h.HandleAsync(new ChangePasswordCommand(_userId, "weak", "weak"), CancellationToken.None))
            .Should().ThrowAsync<WeakPasswordException>();
    }
}
