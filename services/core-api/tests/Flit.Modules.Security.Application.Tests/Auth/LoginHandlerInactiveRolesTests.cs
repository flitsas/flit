using Flit.Modules.Security.Application.Auth.Login;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

/// <summary>
/// HU #10507 — bloqueo de login por roles inactivos. Cubre los 3 AC:
/// AC1) 1 de 2 roles activo → login exitoso, permisos solo del rol activo.
/// AC2) ambos roles inactivos → login rechazado con <see cref="AllRolesInactiveException"/>.
/// AC3) usuario sin ningún rol asignado nunca → login exitoso, sin lanzar la excepción nueva.
/// </summary>
public sealed class LoginHandlerInactiveRolesTests
{
    private readonly IAuthUserRepository _repository = Substitute.For<IAuthUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtTokenIssuer _jwtTokenIssuer = Substitute.For<IJwtTokenIssuer>();
    private readonly LoginHandler _handler;

    public LoginHandlerInactiveRolesTests()
    {
        _handler = new LoginHandler(_repository, _passwordHasher, _jwtTokenIssuer);
    }

    [Fact]
    public async Task HandleAsync_OneOfTwoRolesActive_LogsInWithOnlyActiveRolePermissions()
    {
        var activeRoleId = Guid.NewGuid();
        _repository.FindByEmailAsync("demo@flit.local", Arg.Any<CancellationToken>())
            .Returns(new UserAuthSnapshot
            {
                UserId = Guid.NewGuid(),
                Email = "demo@flit.local",
                Status = "active",
                PasswordHash = "hash",
                TenantId = Guid.NewGuid(),
                // 2 asignaciones totales, pero solo 1 rol sigue activo (el otro fue desactivado).
                ActiveRoles = [new UserRoleSnapshot(activeRoleId, "gestor")],
                TotalAssignedRolesCount = 2,
                PermissionSlugs = ["procedures.read"],
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
        _jwtTokenIssuer.Received(1).IssueToken(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<UserRoleSnapshot>>(roles => roles.Count == 1 && roles[0].Id == activeRoleId),
            Arg.Is<IReadOnlyList<string>>(perms => perms.Count == 1 && perms[0] == "procedures.read"));
    }

    [Fact]
    public async Task HandleAsync_BothRolesInactive_ThrowsAllRolesInactive()
    {
        _repository.FindByEmailAsync("demo@flit.local", Arg.Any<CancellationToken>())
            .Returns(new UserAuthSnapshot
            {
                UserId = Guid.NewGuid(),
                Email = "demo@flit.local",
                Status = "active",
                PasswordHash = "hash",
                TenantId = Guid.NewGuid(),
                // 2 asignaciones totales, ambos roles fueron desactivados -> ActiveRoles vacío.
                ActiveRoles = [],
                TotalAssignedRolesCount = 2,
                PermissionSlugs = [],
            });
        _passwordHasher.Verify("DemoPass1!", "hash").Returns(true);

        var act = () => _handler.HandleAsync(new LoginCommand("demo@flit.local", "DemoPass1!"), CancellationToken.None);

        await act.Should().ThrowAsync<AllRolesInactiveException>();
        _jwtTokenIssuer.DidNotReceiveWithAnyArgs().IssueToken(
            default, default!, default, default!, default!, default!, default!, default!);
    }

    [Fact]
    public async Task HandleAsync_UserNeverHadAnyRoleAssigned_LogsInNormally()
    {
        _repository.FindByEmailAsync("demo@flit.local", Arg.Any<CancellationToken>())
            .Returns(new UserAuthSnapshot
            {
                UserId = Guid.NewGuid(),
                Email = "demo@flit.local",
                Status = "active",
                PasswordHash = "hash",
                TenantId = Guid.NewGuid(),
                // Nunca tuvo ninguna asignación de rol -> TotalAssignedRolesCount == 0.
                ActiveRoles = [],
                TotalAssignedRolesCount = 0,
                PermissionSlugs = [],
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
    }
}
