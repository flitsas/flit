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
/// HU #12422 AC1/AC2/AC3/AC6/AC8 (ADR-0060 D3) — matriz dominio × sujeto del acceso acotado por
/// dominio. Dos redes MARCA_BLANCA (A, B), cada una con cabeza e hija, más una compañía sin red y
/// un SuperAdmin. <c>Uso de ejemplo</c>: <see cref="BuildHandler"/> arma un <see cref="LoginHandler"/>
/// con el dominio de la petición ya fijado (FLIT o una de las dos redes) y <see cref="Login"/>
/// ejecuta el intento con la credencial indicada.
/// </summary>
public sealed class LoginHandlerNetworkScopeTests
{
    private const string Password = "DemoPass1!";
    private const string HostA = "app.red-a.com";
    private const string HostB = "app.red-b.com";

    private static readonly Guid HeadA = Guid.NewGuid();
    private static readonly Guid HeadB = Guid.NewGuid();
    private static readonly Guid ChildOfA = Guid.NewGuid();
    private static readonly Guid ChildOfB = Guid.NewGuid();
    private static readonly Guid NoNetworkTenant = Guid.NewGuid();
    private static readonly Guid ConcesionTenant = Guid.NewGuid();
    private static readonly Guid SuperAdminTenant = HeadA; // el SuperAdmin puede pertenecer a cualquier tenant.

    private readonly IAuthUserRepository _repository = Substitute.For<IAuthUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtTokenIssuer _jwtTokenIssuer = Substitute.For<IJwtTokenIssuer>();
    private readonly IAdminAuditWriter _auditWriter = Substitute.For<IAdminAuditWriter>();
    private readonly ITenantNetworkMembership _networkMembership = Substitute.For<ITenantNetworkMembership>();

    public LoginHandlerNetworkScopeTests()
    {
        _passwordHasher.DummyHash.Returns("dummy-hash");
        _jwtTokenIssuer.IssueToken(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
                Arg.Any<IReadOnlyList<UserRoleSnapshot>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>())
            .Returns(new IssuedAccessToken { Token = "jwt-token", ExpiresInSeconds = 43200 });

        _networkMembership.ResolveAsync(HeadA, Arg.Any<CancellationToken>())
            .Returns(new NetworkMembership(HeadA, true, HostA));
        _networkMembership.ResolveAsync(ChildOfA, Arg.Any<CancellationToken>())
            .Returns(new NetworkMembership(HeadA, true, HostA));
        _networkMembership.ResolveAsync(HeadB, Arg.Any<CancellationToken>())
            .Returns(new NetworkMembership(HeadB, true, HostB));
        _networkMembership.ResolveAsync(ChildOfB, Arg.Any<CancellationToken>())
            .Returns(new NetworkMembership(HeadB, true, HostB));
        _networkMembership.ResolveAsync(NoNetworkTenant, Arg.Any<CancellationToken>())
            .Returns(NetworkMembership.None);
        _networkMembership.ResolveAsync(ConcesionTenant, Arg.Any<CancellationToken>())
            .Returns(NetworkMembership.None); // Concesión: nunca IsMarcaBlancaNetwork.
    }

    public static TheoryData<string, Guid, bool> FlitDomainCases() => new()
    {
        { "cabeza de A", HeadA, false },
        { "hija de A", ChildOfA, false },
        { "cabeza de B", HeadB, false },
        { "hija de B", ChildOfB, false },
        { "sin red", NoNetworkTenant, false },
        { "Concesión", ConcesionTenant, false },
        { "SuperAdmin", SuperAdminTenant, true },
    };

    [Theory]
    [MemberData(nameof(FlitDomainCases))]
    public async Task Login_PorDominioFlit_RedirigeSoloSiEsMarcaBlancaConDominioActivoYNoEsSuperAdmin(
        string caseLabel, Guid tenantId, bool isSuperAdmin)
    {
        var handler = BuildHandler(DomainKind.Flit, host: null, headTenantId: null);
        SetupUser("user@empresa.com", tenantId, isSuperAdmin);

        var act = () => handler.HandleAsync(new LoginCommand("user@empresa.com", Password), CancellationToken.None);

        var expectsRedirect = !isSuperAdmin && (tenantId == HeadA || tenantId == ChildOfA || tenantId == HeadB || tenantId == ChildOfB);
        if (expectsRedirect)
        {
            var expectedHost = tenantId is var t && (t == HeadA || t == ChildOfA) ? HostA : HostB;
            var ex = await act.Should().ThrowAsync<NetworkDomainRequiredException>(caseLabel);
            ex.Which.NetworkDomain.Should().Be(expectedHost, caseLabel);
        }
        else
        {
            await act.Should().NotThrowAsync(caseLabel);
        }
    }

    [Fact]
    public async Task Login_UsuarioInexistente_PorCualquierDominio_LanzaInvalidCredentials()
    {
        var handler = BuildHandler(DomainKind.Network, HostA, HeadA);
        _repository.FindByEmailAsync("ghost@flit.local", Arg.Any<CancellationToken>()).Returns((UserAuthSnapshot?)null);

        var act = () => handler.HandleAsync(new LoginCommand("ghost@flit.local", Password), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
    }

    [Theory]
    [InlineData("cabeza de A", nameof(HeadA))]
    [InlineData("hija de A", nameof(ChildOfA))]
    public async Task Login_PorDominioDeRedA_MiembrosDeAEntranConSesionLigadaAlHost(string label, string tenantField)
    {
        var tenantId = tenantField == nameof(HeadA) ? HeadA : ChildOfA;
        var handler = BuildHandler(DomainKind.Network, HostA, HeadA);
        SetupUser("miembro@red-a.com", tenantId, isSuperAdmin: false);

        var result = await handler.HandleAsync(new LoginCommand("miembro@red-a.com", Password), CancellationToken.None);

        result.AccessToken.Should().Be("jwt-token");
        result.NetworkHost.Should().Be(HostA, label);
        _jwtTokenIssuer.Received(1).IssueToken(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
            Arg.Any<IReadOnlyList<UserRoleSnapshot>>(), Arg.Any<IReadOnlyList<string>>(), HostA);
    }

    // AC8 — "incluido A contra B": un miembro de la red A NUNCA entra por el dominio de B.
    [Theory]
    [InlineData(nameof(HeadA))]
    [InlineData(nameof(ChildOfA))]
    public async Task Login_MiembroDeRedA_PorDominioDeRedB_LanzaInvalidCredentials(string tenantField)
    {
        var tenantId = tenantField == nameof(HeadA) ? HeadA : ChildOfA;
        var handler = BuildHandler(DomainKind.Network, HostB, HeadB);
        SetupUser("miembro@red-a.com", tenantId, isSuperAdmin: false);

        var act = () => handler.HandleAsync(new LoginCommand("miembro@red-a.com", Password), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
    }

    [Theory]
    [InlineData(nameof(NoNetworkTenant))]
    [InlineData(nameof(ConcesionTenant))]
    public async Task Login_SinRedOConcesion_PorDominioDeUnaRed_LanzaInvalidCredentials(string tenantField)
    {
        var tenantId = tenantField == nameof(NoNetworkTenant) ? NoNetworkTenant : ConcesionTenant;
        var handler = BuildHandler(DomainKind.Network, HostA, HeadA);
        SetupUser("otro@empresa.com", tenantId, isSuperAdmin: false);

        var act = () => handler.HandleAsync(new LoginCommand("otro@empresa.com", Password), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
    }

    // AC6 — el SuperAdmin NUNCA se autentica por el dominio de una red, aunque su tenant SEA la cabeza.
    [Fact]
    public async Task Login_SuperAdmin_PorDominioDeUnaRed_LanzaInvalidCredentials()
    {
        var handler = BuildHandler(DomainKind.Network, HostA, HeadA);
        SetupUser("super@flit.local", SuperAdminTenant, isSuperAdmin: true);

        var act = () => handler.HandleAsync(new LoginCommand("super@flit.local", Password), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
    }

    [Theory]
    [InlineData(nameof(HeadA))]
    [InlineData(nameof(NoNetworkTenant))]
    public async Task Login_CredencialInvalida_PorCualquierDominioOSujeto_LanzaInvalidCredentials(string tenantField)
    {
        var tenantId = tenantField == nameof(HeadA) ? HeadA : NoNetworkTenant;
        var handler = BuildHandler(DomainKind.Network, HostA, HeadA);
        SetupUser("user@empresa.com", tenantId, isSuperAdmin: false);
        _passwordHasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var act = () => handler.HandleAsync(new LoginCommand("user@empresa.com", "wrong"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
    }

    private LoginHandler BuildHandler(DomainKind kind, string? host, Guid? headTenantId)
    {
        var domainContextAccessor = Substitute.For<IDomainContextAccessor>();
        domainContextAccessor.Kind.Returns(kind);
        domainContextAccessor.Host.Returns(host);
        domainContextAccessor.HeadTenantId.Returns(headTenantId);

        return new LoginHandler(
            _repository, _passwordHasher, _jwtTokenIssuer, _auditWriter, NullAuditContextAccessor.Instance,
            _networkMembership, domainContextAccessor);
    }

    private void SetupUser(string email, Guid tenantId, bool isSuperAdmin)
    {
        var roles = isSuperAdmin
            ? new List<UserRoleSnapshot> { new(Guid.NewGuid(), "SuperAdmin") }
            : new List<UserRoleSnapshot> { new(Guid.NewGuid(), "demo_admin") };

        _repository.FindByEmailAsync(email, Arg.Any<CancellationToken>())
            .Returns(new UserAuthSnapshot
            {
                UserId = Guid.NewGuid(),
                Email = email,
                Status = "active",
                PasswordHash = "hash",
                TenantId = tenantId,
                ActiveRoles = roles,
                PermissionSlugs = ["auth.me.read"],
            });
        _passwordHasher.Verify(Password, "hash").Returns(true);
    }
}
