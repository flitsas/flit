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
                TenantName = "Acme Renting SAS",
                TenantTaxId = "900123456-7",
                EntityType = "COMPANY",
                ActiveRoles = [new UserRoleSnapshot(roleId, "demo_admin")],
                PermissionSlugs = ["auth.me.read"],
            });
        _passwordHasher.Verify("DemoPass1!", "hash").Returns(true);
        _jwtTokenIssuer.IssueToken(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<UserRoleSnapshot>>(),
                Arg.Any<IReadOnlyList<string>>())
            .Returns(new IssuedAccessToken { Token = "jwt-token", ExpiresInSeconds = 43200 });

        var result = await _handler.HandleAsync(new LoginCommand("demo@flit.local", "DemoPass1!"), CancellationToken.None);

        result.AccessToken.Should().Be("jwt-token");
        result.ExpiresInSeconds.Should().Be(43200);
        result.TokenType.Should().Be("Bearer");

        // AC1 — el handler debe propagar NIT y tipo de entidad al emisor del JWT sin alterarlos.
        _jwtTokenIssuer.Received(1).IssueToken(
            userId,
            "demo@flit.local",
            tenantId,
            "Acme Renting SAS",
            "900123456-7",
            "COMPANY",
            Arg.Is<IReadOnlyList<UserRoleSnapshot>>(r => r.Count == 1 && r[0].Id == roleId),
            Arg.Is<IReadOnlyList<string>>(p => p.Single() == "auth.me.read"));
    }

    [Fact]
    public async Task HandleAsync_TenantTransitOffice_PropagatesEntityType()
    {
        // AC2 — el tipo de entidad TRANSIT_OFFICE también debe propagarse tal cual al emisor.
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repository.FindByEmailAsync("ot@flit.local", Arg.Any<CancellationToken>())
            .Returns(new UserAuthSnapshot
            {
                UserId = userId,
                Email = "ot@flit.local",
                Status = "active",
                PasswordHash = "hash",
                TenantId = tenantId,
                TenantName = "Organismo de Tránsito Norte",
                TenantTaxId = "800987654-1",
                EntityType = "TRANSIT_OFFICE",
                ActiveRoles = [new UserRoleSnapshot(Guid.NewGuid(), "ot_admin")],
            });
        _passwordHasher.Verify("DemoPass1!", "hash").Returns(true);
        _jwtTokenIssuer.IssueToken(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<UserRoleSnapshot>>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(new IssuedAccessToken { Token = "jwt-token", ExpiresInSeconds = 43200 });

        await _handler.HandleAsync(new LoginCommand("ot@flit.local", "DemoPass1!"), CancellationToken.None);

        _jwtTokenIssuer.Received(1).IssueToken(
            userId, "ot@flit.local", tenantId, "Organismo de Tránsito Norte",
            "800987654-1", "TRANSIT_OFFICE",
            Arg.Any<IReadOnlyList<UserRoleSnapshot>>(), Arg.Any<IReadOnlyList<string>>());
    }

    [Fact]
    public async Task HandleAsync_TenantWithoutTaxId_CompletesLoginWithEmptyNit()
    {
        // AC4 — tenant sin NIT registrado: el login se completa sin error y el NIT se propaga vacío.
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repository.FindByEmailAsync("sinnit@flit.local", Arg.Any<CancellationToken>())
            .Returns(new UserAuthSnapshot
            {
                UserId = userId,
                Email = "sinnit@flit.local",
                Status = "active",
                PasswordHash = "hash",
                TenantId = tenantId,
                TenantName = "Tenant Legacy Sin NIT",
                TenantTaxId = string.Empty,
                EntityType = "COMPANY",
                ActiveRoles = [new UserRoleSnapshot(Guid.NewGuid(), "demo_admin")],
            });
        _passwordHasher.Verify("DemoPass1!", "hash").Returns(true);
        _jwtTokenIssuer.IssueToken(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<UserRoleSnapshot>>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(new IssuedAccessToken { Token = "jwt-token", ExpiresInSeconds = 43200 });

        var result = await _handler.HandleAsync(new LoginCommand("sinnit@flit.local", "DemoPass1!"), CancellationToken.None);

        result.AccessToken.Should().Be("jwt-token");
        _jwtTokenIssuer.Received(1).IssueToken(
            userId, "sinnit@flit.local", tenantId, "Tenant Legacy Sin NIT",
            string.Empty, "COMPANY",
            Arg.Any<IReadOnlyList<UserRoleSnapshot>>(), Arg.Any<IReadOnlyList<string>>());
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
                ActiveRoles = [new UserRoleSnapshot(Guid.NewGuid(), "demo_admin")],
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
