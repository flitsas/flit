using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.ActivateAccount;
using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Application.Auth.Network;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

/// <summary>
/// HU #13811 (Feature #13808, Épica #13516) — una invitación pendiente en cualquier compañía bloquea invitar el
/// mismo correo, y la activación revalida que el correo no tenga ya un usuario vivo (409, nunca 500).
/// <para>
/// Uso de ejemplo: con <c>ExistsPendingAsync("juan@x.com")</c> en <c>true</c> (pendiente en la compañía A),
/// invitar <c>juan@x.com</c> desde la compañía B lanza <see cref="InvitationAlreadyPendingException"/>.
/// </para>
/// </summary>
public sealed class HU13811PendienteGlobalYActivacionTests
{
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid InvitedBy = Guid.NewGuid();
    private const string Correo = "juan@x.com";

    // ── AC1 — pendiente en otra compañía ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_Pendiente_en_otra_compania_bloquea_la_invitacion()
    {
        var repo = Substitute.For<IInvitationRepository>();
        repo.RoleExistsInTenantAsync(TenantB, RoleId, Arg.Any<CancellationToken>()).Returns(true);
        repo.ExistsPendingAsync(Correo, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CreateInvitationHandler(
            repo, Substitute.For<ISecureTokenGenerator>(), Substitute.For<IEmailSender>(),
            new InvitationOptions { ActivateUrlBase = "http://localhost:3000/invite/activate" },
            Substitute.For<INetworkUrlBaseResolver>(), Substitute.For<ILogger<CreateInvitationHandler>>());

        var act = () => handler.HandleAsync(
            new CreateInvitationCommand(TenantB, Correo, "Juan", [RoleId], InvitedBy), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvitationAlreadyPendingException>();
        await repo.DidNotReceive().CreateAsync(Arg.Any<UserInvitationData>(), Arg.Any<CancellationToken>());
    }

    // ── AC2/AC3 — activación ────────────────────────────────────────────────────────────────────────

    private sealed class Activacion
    {
        public IInvitationRepository Invitaciones { get; } = Substitute.For<IInvitationRepository>();
        public IUserActivationRepository Activaciones { get; } = Substitute.For<IUserActivationRepository>();
        public IAdminAuditWriter Auditoria { get; } = Substitute.For<IAdminAuditWriter>();
        public ActivateAccountHandler Handler { get; }

        public Activacion()
        {
            var tokens = Substitute.For<ISecureTokenGenerator>();
            tokens.HashToken("raw").Returns("hash");
            var hasher = Substitute.For<IPasswordHasher>();
            hasher.Hash(Arg.Any<string>()).Returns("hashed");
            var email = Substitute.For<IEmailSender>();
            email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).Returns(EmailSendResult.Sent);
            Invitaciones.FindPendingByTokenHashAsync("hash", Arg.Any<CancellationToken>())
                .Returns(new PendingInvitation(Guid.NewGuid(), TenantB, Correo, "Juan", [RoleId], InvitedBy));
            Handler = new ActivateAccountHandler(
                Invitaciones, tokens, Activaciones, hasher, email, Auditoria, NullAuditContextAccessor.Instance,
                Substitute.For<ITenantNetworkMembership>(), Substitute.For<IDomainContextAccessor>(),
                NullLogger<ActivateAccountHandler>.Instance);
        }

        public Task<AccountActivatedResult> ActivarAsync() =>
            Handler.HandleAsync(new ActivateAccountCommand("raw", "FlitPass1!"), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC2_Activar_cuando_el_correo_ya_tiene_usuario_vivo_se_rechaza_sin_crear_usuario()
    {
        var a = new Activacion();
        a.Invitaciones.UserExistsWithEmailAsync(Correo, Arg.Any<CancellationToken>()).Returns(true);

        await a.Invoking(x => x.ActivarAsync()).Should().ThrowAsync<UserAlreadyExistsException>();

        await a.Activaciones.DidNotReceive().ActivateAsync(Arg.Any<ActivationData>(), Arg.Any<CancellationToken>());
        await a.Auditoria.Received(1).WriteAsync(
            Arg.Is<AdminAuditEntry>(e => e.ErrorCode == "email_in_use"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_Carrera_en_la_activacion_se_propaga_como_conflicto_y_se_audita()
    {
        var a = new Activacion();
        a.Activaciones.ActivateAsync(Arg.Any<ActivationData>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new UserAlreadyExistsException()));

        await a.Invoking(x => x.ActivarAsync()).Should().ThrowAsync<UserAlreadyExistsException>();

        await a.Auditoria.Received(1).WriteAsync(
            Arg.Is<AdminAuditEntry>(e => e.ErrorCode == "email_in_use"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Contrato_activacion_sin_conflicto_sigue_creando_el_usuario()
    {
        var a = new Activacion();

        await a.ActivarAsync();

        await a.Activaciones.Received(1).ActivateAsync(
            Arg.Is<ActivationData>(d => d.Email == Correo), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Contrato_codigo_y_mensaje_de_conflicto_son_unicos()
    {
        EmailConflict.Code.Should().Be("EMAIL_ALREADY_IN_USE");
        EmailConflict.Message.Should().NotBeNullOrWhiteSpace();
    }
}
