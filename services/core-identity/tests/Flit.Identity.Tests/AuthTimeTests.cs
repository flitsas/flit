using System.Security.Claims;
using Flit.Api.Identity;
using Flit.Modules.Security.Application.Products;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Xunit;

namespace Flit.Identity.Tests;

/// <summary>
/// Feature #14453: <c>auth_time</c> es el inicio real de la sesión del usuario. El emisor lo fija al iniciar
/// sesión y lo CONSERVA en cada refresh (un <c>iat</c> se renueva en cada refresh y no sirve de tope).
/// </summary>
public sealed class AuthTimeTests
{
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly Guid Rol = Guid.NewGuid();

    private sealed class Usuarios : IAuthUserRepository
    {
        public Task<UserAuthSnapshot?> FindByEmailAsync(string email, CancellationToken ct) => Task.FromResult<UserAuthSnapshot?>(new UserAuthSnapshot
        {
            UserId = Usuario, Email = email, Status = "active", TenantId = Guid.NewGuid(), TenantName = "OT",
            ActiveRoles = [new UserRoleSnapshot(Rol, "gestor_ot")], TotalAssignedRolesCount = 1,
        });
    }

    private sealed class Acceso : IProductAccessResolver
    {
        public Task<ProductAccess> ResolveAsync(Guid userId, Guid tenantId, string productCode, CancellationToken ct) =>
            Task.FromResult(new ProductAccess(true, [new RoleRef(Rol, "gestor_ot")], []));
    }

    private static ClaimsPrincipal Fuente(string? authTime)
    {
        var id = new ClaimsIdentity("test");
        id.AddClaim(new Claim("sub", Usuario.ToString()));
        id.AddClaim(new Claim("email", "cajero@ot.test"));
        if (authTime is not null)
            id.AddClaim(new Claim("auth_time", authTime));
        return new ClaimsPrincipal(id);
    }

    [Fact]
    public async Task ElRefreshConservaElAuthTimeDelInicioDeSesion()
    {
        var reloj = new RelojFijo(new DateTimeOffset(2026, 11, 10, 20, 0, 0, TimeSpan.Zero));
        var fabrica = new OidcPrincipalFactory(new Usuarios(), new Acceso(), reloj);
        var inicio = reloj.GetUtcNow().AddHours(-11).ToUnixTimeSeconds();

        var grant = await fabrica.ForUserAsync(Fuente(inicio.ToString()), "tramites", ["openid"], CancellationToken.None);

        grant.Principal!.FindFirstValue("auth_time").Should().Be(inicio.ToString());
    }

    [Fact]
    public async Task UnaSesionSinAuthTimeArrancaAhora()
    {
        var reloj = new RelojFijo(new DateTimeOffset(2026, 11, 10, 20, 0, 0, TimeSpan.Zero));
        var fabrica = new OidcPrincipalFactory(new Usuarios(), new Acceso(), reloj);

        var grant = await fabrica.ForUserAsync(Fuente(null), "tramites", ["openid"], CancellationToken.None);

        grant.Principal!.FindFirstValue("auth_time").Should().Be(reloj.GetUtcNow().ToUnixTimeSeconds().ToString());
    }

    [Fact]
    public async Task UnAuthTimeInvalidoNoSeCopia()
    {
        var reloj = new RelojFijo(new DateTimeOffset(2026, 11, 10, 20, 0, 0, TimeSpan.Zero));
        var fabrica = new OidcPrincipalFactory(new Usuarios(), new Acceso(), reloj);

        var grant = await fabrica.ForUserAsync(Fuente("no-es-numero"), "tramites", ["openid"], CancellationToken.None);

        grant.Principal!.FindFirstValue("auth_time").Should().Be(reloj.GetUtcNow().ToUnixTimeSeconds().ToString());
    }
}

internal sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => ahora;
}
