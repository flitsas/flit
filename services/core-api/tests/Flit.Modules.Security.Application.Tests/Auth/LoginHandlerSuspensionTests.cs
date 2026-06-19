using Flit.Modules.Security.Application.Auth.Login;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

public sealed class LoginHandlerSuspensionTests
{
    private readonly IAuthUserRepository _repository = Substitute.For<IAuthUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtTokenIssuer _jwtTokenIssuer = Substitute.For<IJwtTokenIssuer>();
    private readonly LoginHandler _handler;

    public LoginHandlerSuspensionTests()
    {
        _handler = new LoginHandler(_repository, _passwordHasher, _jwtTokenIssuer);
    }

    [Fact]
    public async Task HandleAsync_ValidPasswordButTemporarilySuspended_ThrowsAccountSuspended()
    {
        _repository.FindByEmailAsync("demo@flit.local", Arg.Any<CancellationToken>())
            .Returns(new UserAuthSnapshot
            {
                UserId = Guid.NewGuid(),
                Email = "demo@flit.local",
                Status = "active",
                PasswordHash = "hash",
                TenantId = Guid.NewGuid(),
                RoleId = Guid.NewGuid(),
                RoleCode = "demo_admin",
                IsTemporarilySuspended = true,
            });
        _passwordHasher.Verify("DemoPass1!", "hash").Returns(true);

        await _handler.Invoking(h => h.HandleAsync(new LoginCommand("demo@flit.local", "DemoPass1!"), CancellationToken.None))
            .Should().ThrowAsync<AccountSuspendedException>();

        _jwtTokenIssuer.DidNotReceiveWithAnyArgs().IssueToken(
            default, default!, default, default, default!, default!);
    }
}
