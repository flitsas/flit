using Flit.Ict.Application.Auth.Login;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Auth;

public sealed class LoginIntegrationClientHandlerTests
{
    private readonly IIntegrationClientRepository _clients = Substitute.For<IIntegrationClientRepository>();
    private readonly IIctPasswordHasher _hasher = Substitute.For<IIctPasswordHasher>();
    private readonly ITenantDirectory _tenants = Substitute.For<ITenantDirectory>();
    private readonly IIctJwtTokenIssuer _issuer = Substitute.For<IIctJwtTokenIssuer>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LoginIntegrationClientHandler CreateHandler() => new(_clients, _hasher, _tenants, _issuer);

    private static IntegrationClient ActiveClient() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Username = "gestor",
        PasswordHash = "argon2id$hash",
        IsActive = true,
        Scopes = "[\"ict.transactions.write\"]",
    };

    [Fact]
    public async Task Valid_credentials_issues_token()
    {
        var client = ActiveClient();
        _clients.FindByUsernameAsync("gestor", Arg.Any<CancellationToken>()).Returns(client);
        _hasher.Verify("secret", client.PasswordHash).Returns(true);
        _tenants.GetAsync(client.TenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantInfo(client.TenantId, "T1", "Compañía SAS", "901698038", IsActive: true));
        _issuer.Issue(client.Id, client.TenantId, "Compañía SAS", "901698038", Arg.Any<IReadOnlyList<string>>())
            .Returns(new IssuedIctToken("jwt-token", 7200));

        var (result, error) = await CreateHandler().HandleAsync(
            new LoginIntegrationClientCommand("gestor", "secret", CompanyManagerId: 994), Ct);

        error.Should().BeNull();
        result!.Token.Should().Be("jwt-token");
        result.MustRotate.Should().BeFalse();
    }

    [Fact]
    public async Task Wrong_password_returns_invalid_credentials_and_increments_attempts()
    {
        var client = ActiveClient();
        _clients.FindByUsernameAsync("gestor", Arg.Any<CancellationToken>()).Returns(client);
        _hasher.Verify("bad", client.PasswordHash).Returns(false);

        var (result, error) = await CreateHandler().HandleAsync(
            new LoginIntegrationClientCommand("gestor", "bad", CompanyManagerId: null), Ct);

        result.Should().BeNull();
        error.Should().Be("invalid_credentials");
        client.FailedLoginAttempts.Should().Be(1);
        await _clients.Received(1).SaveAsync(client, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_user_does_dummy_verify_and_returns_invalid_credentials()
    {
        _clients.FindByUsernameAsync("ghost", Arg.Any<CancellationToken>()).Returns((IntegrationClient?)null);

        var (result, error) = await CreateHandler().HandleAsync(
            new LoginIntegrationClientCommand("ghost", "secret", CompanyManagerId: null), Ct);

        result.Should().BeNull();
        error.Should().Be("invalid_credentials");
        _hasher.Received(1).Verify("secret", null);
    }

    [Fact]
    public async Task Locked_account_returns_locked()
    {
        var client = ActiveClient();
        client.LockedUntil = DateTime.UtcNow.AddMinutes(10);
        _clients.FindByUsernameAsync("gestor", Arg.Any<CancellationToken>()).Returns(client);

        var (result, error) = await CreateHandler().HandleAsync(
            new LoginIntegrationClientCommand("gestor", "secret", CompanyManagerId: null), Ct);

        result.Should().BeNull();
        error.Should().Be("locked");
    }

    [Fact]
    public async Task Must_rotate_returns_flag_without_token()
    {
        var client = ActiveClient();
        client.MustRotate = true;
        _clients.FindByUsernameAsync("gestor", Arg.Any<CancellationToken>()).Returns(client);
        _hasher.Verify("secret", client.PasswordHash).Returns(true);

        var (result, error) = await CreateHandler().HandleAsync(
            new LoginIntegrationClientCommand("gestor", "secret", CompanyManagerId: null), Ct);

        error.Should().BeNull();
        result!.MustRotate.Should().BeTrue();
        result.Token.Should().BeNull();
    }
}
