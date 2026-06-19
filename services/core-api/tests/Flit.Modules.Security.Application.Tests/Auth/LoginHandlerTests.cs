using Flit.Modules.Security.Application.Auth.Login;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

public sealed class LoginHandlerTests
{
    private readonly IAuthUserRepository _repository = Substitute.For<IAuthUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtTokenIssuer _jwtTokenIssuer = Substitute.For<IJwtTokenIssuer>();
    private readonly LoginHandler _handler;

    public LoginHandlerTests()
    {
        _handler = new LoginHandler(_repository, _passwordHasher, _jwtTokenIssuer);
    }

    [Fact]
    public async Task HandleAsync_ValidCredentials_ReturnsJwt()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        _repository.FindByEmailAsync("demo@flit.local", Arg.Any<CancellationToken>())
            .Returns(new UserAuthSnapshot
            {
                UserId = userId,
                Email = "demo@flit.local",
                Status = "active",
                PasswordHash = "hash",
                TenantId = tenantId,
                RoleId = roleId,
                RoleCode = "demo_admin",
                PermissionSlugs = ["auth.me.read"],
            });
        _passwordHasher.Verify("DemoPass1!", "hash").Returns(true);
        _jwtTokenIssuer.IssueToken(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>())
            .Returns(new IssuedAccessToken { Token = "jwt-token", ExpiresInSeconds = 43200 });

        var result = await _handler.HandleAsync(new LoginCommand("demo@flit.local", "DemoPass1!"), CancellationToken.None);

        result.AccessToken.Should().Be("jwt-token");
        result.ExpiresInSeconds.Should().Be(43200);
        result.TokenType.Should().Be("Bearer");
    }

    [Fact]
    public async Task HandleAsync_InvalidPassword_ThrowsInvalidCredentials()
    {
        _repository.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserAuthSnapshot
            {
                UserId = Guid.NewGuid(),
                Email = "demo@flit.local",
                Status = "active",
                PasswordHash = "hash",
                TenantId = Guid.NewGuid(),
                RoleId = Guid.NewGuid(),
                RoleCode = "demo_admin",
            });
        _passwordHasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var act = () => _handler.HandleAsync(new LoginCommand("demo@flit.local", "wrong"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
    }

    [Fact]
    public async Task HandleAsync_UnknownEmail_ThrowsInvalidCredentials()
    {
        _repository.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((UserAuthSnapshot?)null);

        var act = () => _handler.HandleAsync(new LoginCommand("missing@flit.local", "x"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
    }
}
