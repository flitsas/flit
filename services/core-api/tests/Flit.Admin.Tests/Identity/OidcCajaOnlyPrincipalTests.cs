using System.Security.Claims;
using Flit.Api.Identity;
using Flit.Modules.Security.Application.Products;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using NSubstitute;
using OpenIddict.Abstractions;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #14454 (Épica #14210, ADR-0082) — primera capa del guardián solo-caja: Identidad no emite token del hub ni de Trámites a quien
/// solo tiene <c>ot_caja</c> (<c>CAJA_ONLY</c>), le emite el del producto <c>caja</c>, y no cambia nada para ningún otro usuario.
/// </summary>
public sealed class OidcCajaOnlyPrincipalTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string Email = "caja@ot.test";

    private readonly IAuthUserRepository _users = Substitute.For<IAuthUserRepository>();
    private readonly IProductAccessResolver _access = Substitute.For<IProductAccessResolver>();
    private readonly OidcPrincipalFactory _factory;

    public OidcCajaOnlyPrincipalTests()
    {
        _factory = new OidcPrincipalFactory(_users, _access);
        _access.ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var product = ci.ArgAt<string>(2);
                // El rol de caja es del producto caja; los roles de OT (gestor_ot, ot_admin) son de trámites.
                return product switch
                {
                    "caja" => new ProductAccess(true, [new RoleRef(Guid.NewGuid(), "ot_caja")], ["caja.cobrar"]),
                    "tramites" => new ProductAccess(true, [new RoleRef(Guid.NewGuid(), "gestor_ot")], ["ot.tramites.manage"]),
                    _ => new ProductAccess(true, [], []),
                };
            });
    }

    private void Usuario(params string[] roles) =>
        _users.FindByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(new UserAuthSnapshot
        {
            UserId = UserId,
            Email = Email,
            Status = "active",
            TenantId = TenantId,
            EntityType = "TRANSIT_OFFICE",
            ActiveRoles = [.. roles.Select(r => new UserRoleSnapshot(Guid.NewGuid(), r))],
            TotalAssignedRolesCount = roles.Length,
        });

    private static ClaimsPrincipal Sesion() =>
        new(new ClaimsIdentity(
        [
            new Claim(OpenIddictConstants.Claims.Subject, UserId.ToString()),
            new Claim(OpenIddictConstants.Claims.Email, Email),
        ], "hub"));

    [Theory]
    [InlineData("plataforma")]
    [InlineData("tramites")]
    [InlineData("comparendos")]
    public async Task UsuarioSoloCaja_NoRecibeTokenDeOtroProducto(string producto)
    {
        Usuario("ot_caja");

        var grant = await _factory.ForUserAsync(Sesion(), producto, ["openid"], CancellationToken.None);

        grant.Principal.Should().BeNull();
        grant.DeniedCode.Should().Be("CAJA_ONLY");
    }

    [Fact]
    public async Task UsuarioSoloCaja_RecibeTokenDelProductoCaja()
    {
        Usuario("ot_caja");

        var grant = await _factory.ForUserAsync(Sesion(), "caja", ["openid"], CancellationToken.None);

        grant.DeniedCode.Should().BeNull();
        grant.Principal!.FindFirstValue("product").Should().Be("caja");
        grant.Principal.FindAll(OpenIddictConstants.Claims.Role).Select(c => c.Value).Should().Equal("ot_caja");
    }

    [Theory]
    [InlineData("plataforma")]
    [InlineData("tramites")]
    public async Task OtAdminYOtrosRolesDelOrganismo_SiguenRecibiendoSuToken(string producto)
    {
        Usuario("gestor_ot");

        var grant = await _factory.ForUserAsync(Sesion(), producto, ["openid"], CancellationToken.None);

        grant.DeniedCode.Should().BeNull();
        grant.Principal.Should().NotBeNull();
    }

    [Fact]
    public async Task CajaMasOtroRol_NoEsSoloCaja_SigueRecibiendoElTokenDeTramites()
    {
        Usuario("ot_caja", "gestor_ot");

        var grant = await _factory.ForUserAsync(Sesion(), "tramites", ["openid"], CancellationToken.None);

        grant.DeniedCode.Should().BeNull();
    }

    [Fact]
    public async Task UsuarioQueNoEsDeCaja_NoRecibeTokenDelProductoCaja()
    {
        _access.ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), "caja", Arg.Any<CancellationToken>()).Returns(new ProductAccess(true, [], []));
        Usuario("gestor_ot");

        var grant = await _factory.ForUserAsync(Sesion(), "caja", ["openid"], CancellationToken.None);

        grant.Principal.Should().BeNull();
        grant.DeniedCode.Should().Be(OidcPrincipalFactory.ProductRoleRequired);
    }
}
