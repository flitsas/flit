using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.Login;
using Flit.Modules.Security.Application.Auth.Network;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

/// <summary>
/// HU #14454 (Épica #14210, ADR-0082) — el JWT heredado de <c>/api/v1/auth/login</c> abre FLIT 2.0: quien solo tiene el rol
/// <c>ot_caja</c> no lo recibe; cualquier otro usuario (incluido quien tiene <c>ot_caja</c> y otro rol) sigue entrando.
/// </summary>
public sealed class LoginHandlerCajaOnlyTests
{
    private readonly IAuthUserRepository _repository = Substitute.For<IAuthUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtTokenIssuer _jwtTokenIssuer = Substitute.For<IJwtTokenIssuer>();
    private readonly LoginHandler _handler;

    public LoginHandlerCajaOnlyTests()
    {
        _handler = new LoginHandler(
            _repository, _passwordHasher, _jwtTokenIssuer, Substitute.For<IAdminAuditWriter>(), NullAuditContextAccessor.Instance,
            Substitute.For<ITenantNetworkMembership>(), Substitute.For<IDomainContextAccessor>());
        _passwordHasher.Verify("Clave1!", "hash").Returns(true);
        _jwtTokenIssuer.IssueToken(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<bool>(), Arg.Any<IReadOnlyList<UserRoleSnapshot>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>())
            .Returns(new IssuedAccessToken { Token = "jwt", ExpiresInSeconds = 3600 });
    }

    private void Usuario(params string[] roles) =>
        _repository.FindByEmailAsync("caja@ot.test", Arg.Any<CancellationToken>())
            .Returns(new UserAuthSnapshot
            {
                UserId = Guid.NewGuid(),
                Email = "caja@ot.test",
                Status = "active",
                PasswordHash = "hash",
                TenantId = Guid.NewGuid(),
                ActiveRoles = [.. roles.Select(r => new UserRoleSnapshot(Guid.NewGuid(), r))],
                TotalAssignedRolesCount = roles.Length,
            });

    [Fact]
    public async Task UsuarioSoloCaja_NoRecibeElJwtHeredado()
    {
        Usuario("ot_caja");

        var act = () => _handler.HandleAsync(new LoginCommand("caja@ot.test", "Clave1!"), CancellationToken.None);

        await act.Should().ThrowAsync<CajaOnlyAccountException>();
        _jwtTokenIssuer.DidNotReceiveWithAnyArgs().IssueToken(
            default, default!, default, default!, default!, default!, default!, default, default!, default!, default!);
    }

    [Theory]
    [InlineData("ot_admin")]
    [InlineData("gestor_ot")]
    [InlineData("ot_caja", "gestor_ot")]
    public async Task OtrosUsuarios_SiguenRecibiendoElJwt(params string[] roles)
    {
        Usuario(roles);

        var result = await _handler.HandleAsync(new LoginCommand("caja@ot.test", "Clave1!"), CancellationToken.None);

        result.AccessToken.Should().Be("jwt");
    }

    [Fact]
    public async Task UsuarioSinRoles_SigueEntrandoComoAntes()
    {
        Usuario();

        var result = await _handler.HandleAsync(new LoginCommand("caja@ot.test", "Clave1!"), CancellationToken.None);

        result.AccessToken.Should().Be("jwt");
    }

    [Theory]
    [InlineData(new[] { "ot_caja" }, true)]
    [InlineData(new[] { "OT_CAJA" }, true)]
    [InlineData(new[] { "ot_caja", "ot_admin" }, false)]
    [InlineData(new[] { "ot_admin" }, false)]
    [InlineData(new string[0], false)]
    public void CajaOnly_IsCajaOnly(string[] roles, bool esperado) =>
        CajaOnly.IsCajaOnly(roles).Should().Be(esperado);
}
