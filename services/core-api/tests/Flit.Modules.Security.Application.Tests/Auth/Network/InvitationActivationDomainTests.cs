using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.ActivateAccount;
using Flit.Modules.Security.Application.Auth.Network;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth.Network;

/// <summary>
/// HU #12423 AC5 — coherencia entre el dominio sellado de la petición y la red del tenant dueño
/// del token de activación. Un enlace de red abierto en otro dominio (FLIT u otra red) responde el
/// MISMO error genérico de token inválido de hoy (<see cref="InvalidInvitationTokenException"/>),
/// sin revelar a qué red pertenece.
/// <para>
/// Uso de ejemplo:
/// var handler = new ActivateAccountHandler(..., networkMembership, domainContext, ...);
/// await handler.HandleAsync(new ActivateAccountCommand(token, password), ct); // throws si incoherente
/// </para>
/// </summary>
public sealed class InvitationActivationDomainTests
{
    private readonly IInvitationRepository _invitationRepo = Substitute.For<IInvitationRepository>();
    private readonly ISecureTokenGenerator _tokenGen = Substitute.For<ISecureTokenGenerator>();
    private readonly IUserActivationRepository _activationRepo = Substitute.For<IUserActivationRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IAdminAuditWriter _auditWriter = Substitute.For<IAdminAuditWriter>();
    private readonly IAuditContextAccessor _auditContext = NullAuditContextAccessor.Instance;
    private readonly ITenantNetworkMembership _networkMembership = Substitute.For<ITenantNetworkMembership>();
    private readonly IDomainContextAccessor _domainContext = Substitute.For<IDomainContextAccessor>();

    private static readonly Guid InvitationId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid InvitedBy = Guid.NewGuid();
    private const string RawToken = "raw-token-abc";
    private const string TokenHash = "hash-abc";
    private const string ValidPassword = "FlitPass1!";

    private readonly PendingInvitation _pendingInvitation = new(
        InvitationId, TenantId, "invited@flit.local", "Usuario Invitado", [RoleId], InvitedBy);

    private ActivateAccountHandler Handler => new(
        _invitationRepo, _tokenGen, _activationRepo, _hasher, _emailSender, _auditWriter, _auditContext,
        _networkMembership, _domainContext, NullLogger<ActivateAccountHandler>.Instance);

    public InvitationActivationDomainTests()
    {
        _tokenGen.HashToken(RawToken).Returns(TokenHash);
        _hasher.Hash(ValidPassword).Returns("hashed-password");
        _emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).Returns(EmailSendResult.Sent);
        _invitationRepo.FindPendingByTokenHashAsync(TokenHash, Arg.Any<CancellationToken>())
            .Returns(_pendingInvitation);
    }

    // Camino feliz de hoy (compañía sin red): dominio FLIT, tenant sin red ⇒ activa sin problema.
    [Fact]
    public async Task HandleAsync_FlitDomainTenantWithoutNetwork_Activates()
    {
        _domainContext.Kind.Returns(DomainKind.Flit);
        _networkMembership.ResolveAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(NetworkMembership.None);

        var result = await Handler.HandleAsync(new ActivateAccountCommand(RawToken, ValidPassword), CancellationToken.None);

        result.Should().NotBeNull();
        await _activationRepo.Received(1).ActivateAsync(Arg.Any<ActivationData>(), Arg.Any<CancellationToken>());
    }

    // AC5 — enlace de la red R abierto en el dominio de FLIT ⇒ mismo error genérico, sin activar.
    [Fact]
    public async Task HandleAsync_NetworkInvitationOpenedOnFlitDomain_ThrowsGenericInvalidToken()
    {
        var headTenantId = Guid.NewGuid();
        _domainContext.Kind.Returns(DomainKind.Flit);
        _networkMembership.ResolveAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new NetworkMembership(headTenantId, IsMarcaBlancaNetwork: true, ActiveHost: "app.movilidadandina.com"));

        await Handler
            .Invoking(h => h.HandleAsync(new ActivateAccountCommand(RawToken, ValidPassword), CancellationToken.None))
            .Should().ThrowAsync<InvalidInvitationTokenException>();

        await _activationRepo.DidNotReceiveWithAnyArgs().ActivateAsync(Arg.Any<ActivationData>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceiveWithAnyArgs().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // AC5 — enlace de la red R abierto en el dominio de otra red R' ⇒ mismo error genérico.
    [Fact]
    public async Task HandleAsync_NetworkInvitationOpenedOnOtherNetworkDomain_ThrowsGenericInvalidToken()
    {
        var ownHeadTenantId = Guid.NewGuid();
        var otherHeadTenantId = Guid.NewGuid();
        _domainContext.Kind.Returns(DomainKind.Network);
        _domainContext.HeadTenantId.Returns(otherHeadTenantId);
        _networkMembership.ResolveAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new NetworkMembership(ownHeadTenantId, IsMarcaBlancaNetwork: true, ActiveHost: "app.movilidadandina.com"));

        await Handler
            .Invoking(h => h.HandleAsync(new ActivateAccountCommand(RawToken, ValidPassword), CancellationToken.None))
            .Should().ThrowAsync<InvalidInvitationTokenException>();

        await _activationRepo.DidNotReceiveWithAnyArgs().ActivateAsync(Arg.Any<ActivationData>(), Arg.Any<CancellationToken>());
    }

    // Camino feliz de red: enlace de la red R abierto en el dominio de la MISMA red R ⇒ activa.
    [Fact]
    public async Task HandleAsync_NetworkInvitationOpenedOnOwnNetworkDomain_Activates()
    {
        var headTenantId = Guid.NewGuid();
        _domainContext.Kind.Returns(DomainKind.Network);
        _domainContext.HeadTenantId.Returns(headTenantId);
        _networkMembership.ResolveAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new NetworkMembership(headTenantId, IsMarcaBlancaNetwork: true, ActiveHost: "app.movilidadandina.com"));

        var result = await Handler.HandleAsync(new ActivateAccountCommand(RawToken, ValidPassword), CancellationToken.None);

        result.Should().NotBeNull();
        await _activationRepo.Received(1).ActivateAsync(Arg.Any<ActivationData>(), Arg.Any<CancellationToken>());
    }
}
